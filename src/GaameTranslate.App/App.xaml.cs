using System.Net.Http;
using System.Windows.Interop;
using GaameTranslate.Core;
using GaameTranslate.Infrastructure;
using GaameTranslate.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Forms = System.Windows.Forms;

namespace GaameTranslate.App;

public partial class App : System.Windows.Application
{
    private IHost? host;
    private Mutex? singleInstance;
    private EventWaitHandle? activation;
    private RegisteredWaitHandle? activationWait;
    private Forms.NotifyIcon? trayIcon;
    private GlobalHotkeys? hotkeys;
    private ControllerShortcuts? controller;
    private OverlayManager? overlays;
    private MainViewModel? vm;
    private MainWindow? main;
    private bool exiting, shortcutsPaused, restoreMain;
    public static string DataDirectory { get; private set; } = "";
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Named handles are scoped to this Windows login session; another launch activates the existing window.
        singleInstance = new Mutex(true, "Local\\GaameTranslate.Application.v1", out var created);
        if (!created)
        {
            try { using var existing = EventWaitHandle.OpenExisting("Local\\GaameTranslate.Activate.v1"); existing.Set(); } catch (WaitHandleCannotBeOpenedException) { }
            Shutdown(); return;
        }
        activation = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\GaameTranslate.Activate.v1");
        activationWait = ThreadPool.RegisterWaitForSingleObject(activation, (_, _) => Dispatcher.BeginInvoke(ShowMain), null, Timeout.Infinite, false);
        try
        {
            DataDirectory = ReadDataDirectory(e.Args); Directory.CreateDirectory(DataDirectory);
            var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = [], ContentRootPath = AppContext.BaseDirectory });
            builder.Logging.ClearProviders(); builder.Logging.AddProvider(new DiagnosticsLoggerProvider(DataDirectory));
            builder.Services.AddSingleton<ISettingsStore>(new SettingsStore(DataDirectory)); builder.Services.AddSingleton(new LocalDatabase(DataDirectory));
            builder.Services.AddSingleton<ICredentialStore>(new WindowsCredentialStore(Path.Combine(DataDirectory, "credentials")));
            builder.Services.AddSingleton<IScreenCapture, DesktopCapture>(); builder.Services.AddSingleton<IOcrProvider, AdaptiveOcrProvider>(); builder.Services.AddSingleton<TranslationCache>();
            builder.Services.AddSingleton<ISpeechService, WindowsSpeechService>();
            builder.Services.AddSingleton(new OfflineDictionaryStore(Path.Combine(DataDirectory, "dictionaries")));
            builder.Services.AddSingleton(new LocalMediaStore(Path.Combine(DataDirectory, "media")));
            builder.Services.AddSingleton<DictionaryService>();
            builder.Services.AddSingleton(_ => new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan });
            builder.Services.AddSingleton<MainViewModel>(); builder.Services.AddSingleton<OverlayManager>(); host = builder.Build();
            await host.StartAsync(); vm = host.Services.GetRequiredService<MainViewModel>(); await vm.InitializeAsync(); ApplyTheme(vm.Settings.LightTheme);
            main = new MainWindow(vm); MainWindow = main; overlays = host.Services.GetRequiredService<OverlayManager>(); var selector = new RegionSelector(); vm.SelectRegion = selector.SelectAsync;
            vm.SessionReady += overlays.Show; vm.ToggleOverlayRequested += overlays.Toggle; vm.InteractionRequested += overlays.ToggleInteraction;
            vm.WordLookupRequested += overlays.Lookup;
            vm.IsScreenOverlayVisible = () => overlays.IsVisible; vm.CloseOverlayRequested += overlays.Close;
            vm.OperationFailed += message => { if (main is { IsVisible: false }) trayIcon?.ShowBalloonTip(5000, "GaameTranslate", message, Forms.ToolTipIcon.Warning); };
            vm.CaptureVisibilityChanged += hidden =>
            {
                if (hidden) { restoreMain = main.IsVisible; main.Hide(); overlays.HideForCapture(); }
                else if (!exiting) { if (restoreMain) { main.ShowActivated = false; main.Show(); main.ShowActivated = true; } overlays.RestoreAfterCapture(); }
            };
            hotkeys = new GlobalHotkeys(); hotkeys.Invoked += OnHotkey;
            controller = new ControllerShortcuts(); controller.Invoked += OnHotkey; controller.SetEnabled(vm.Settings.ControllerEnabled);
            vm.SettingsChanged += () => { RegisterHotkeys(); controller.SetEnabled(!shortcutsPaused && vm.Settings.ControllerEnabled); ApplyTheme(vm.Settings.LightTheme); NativeMethods.StyleWindow(new WindowInteropHelper(main).EnsureHandle(), !vm.Settings.LightTheme); if (vm.LastSession is { CaptureType: not "Text" } last) overlays.Show(last); };
            CreateTray(); RegisterHotkeys(); if (!vm.Settings.StartMinimized || e.Args.Contains("--show")) main.Show();
            if (e.Args.Contains("--smoke-test"))
            {
                // Real native OCR, GDI and DPAPI diagnostic. Does not contact translation services.
                var report = await NativeSmokeTest.RunAsync(host.Services.GetRequiredService<IOcrProvider>(), host.Services.GetRequiredService<IScreenCapture>(), host.Services.GetRequiredService<ICredentialStore>());
                await File.WriteAllTextAsync(Path.Combine(DataDirectory, "native-smoke.json"), report); ExitApplication();
            }
        }
        catch (Exception error)
        {
            System.Windows.MessageBox.Show(error is UserFacingException ? error.Message : "GaameTranslate could not start. Check access to its data folder and try again.\n\n" + error.GetType().Name, "GaameTranslate", MessageBoxButton.OK, MessageBoxImage.Error); ExitApplication();
        }
    }
    private static string ReadDataDirectory(string[] args)
    {
        var index = Array.IndexOf(args, "--data-dir");
        return index >= 0 && index + 1 < args.Length ? Path.GetFullPath(args[index + 1]) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GaameTranslate");
    }
    private void RegisterHotkeys()
    {
        if (shortcutsPaused || hotkeys is null || vm is null) return;
        var conflicts = hotkeys.Register(vm.Settings.Hotkeys); if (conflicts.Count > 0) vm.Status = "Shortcut conflict: " + string.Join("; ", conflicts) + ". Choose different combinations in Settings.";
    }
    private async void OnHotkey(string action)
    {
        if (vm is null || overlays is null) return;
        await vm.GuardAsync(async () =>
        {
            switch (action)
            {
                case "Region": await vm.RunCaptureAsync("Region"); break;
                case "Fullscreen": await vm.RunCaptureAsync("Fullscreen"); break;
                case "Repeat": await vm.RunCaptureAsync("Repeat"); break;
                case "ScreenTranslation": await vm.ScreenTranslationAsync(); break;
                case "WordLookup": await vm.WordAtCursorAsync(); break;
                case "ToggleOverlay": overlays.Toggle(); break;
                case "Interaction": overlays.ToggleInteraction(); break;
                case "Vocabulary": vm.PageIndex = 2; ShowMain(); break;
            }
        });
    }
    private void CreateTray()
    {
        trayIcon = new Forms.NotifyIcon { Text = "GaameTranslate · ready", Visible = true };
        using var process = System.Diagnostics.Process.GetCurrentProcess(); trayIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(process.MainModule!.FileName!) ?? System.Drawing.SystemIcons.Application;
        var menu = new Forms.ContextMenuStrip(); menu.Items.Add("Open GaameTranslate", null, (_, _) => ShowMain()); menu.Items.Add("Translate a region", null, (_, _) => OnHotkey("Region")); menu.Items.Add("Translate fullscreen", null, (_, _) => OnHotkey("Fullscreen")); menu.Items.Add("Repeat last region", null, (_, _) => OnHotkey("Repeat")); menu.Items.Add("Vocabulary", null, (_, _) => OnHotkey("Vocabulary"));
        menu.Items.Add("Show / hide overlay", null, (_, _) => overlays?.Toggle()); menu.Items.Add("Toggle overlay interaction", null, (_, _) => overlays?.ToggleInteraction()); menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Экранный переводчик", null, (_, _) => OnHotkey("ScreenTranslation")); menu.Items.Add("Слово под курсором", null, (_, _) => OnHotkey("WordLookup"));
        var pause = new Forms.ToolStripMenuItem("Pause shortcuts"); pause.Click += (_, _) => { shortcutsPaused = !shortcutsPaused; pause.Text = shortcutsPaused ? "Resume shortcuts" : "Pause shortcuts"; controller?.SetEnabled(!shortcutsPaused && vm?.Settings.ControllerEnabled == true); if (shortcutsPaused) { hotkeys?.Unregister(); vm?.StopMonitoring(); } else RegisterHotkeys(); }; menu.Items.Add(pause);
        menu.Items.Add("Exit", null, (_, _) => ExitApplication()); trayIcon.ContextMenuStrip = menu; trayIcon.DoubleClick += (_, _) => ShowMain();
    }
    public void ShowMain() { if (main is null) return; main.Show(); main.WindowState = WindowState.Normal; main.Activate(); }
    private void ApplyTheme(bool light)
    {
        var colors = light ? new Dictionary<string, string> { ["BackgroundBrush"] = "#F4F6F8", ["SidebarBrush"] = "#ECEFF3", ["SurfaceBrush"] = "#FFFFFF", ["HoverBrush"] = "#E3EBE8", ["BorderBrush"] = "#CDD5DD", ["TextBrush"] = "#17232D", ["MutedBrush"] = "#526471", ["AccentBrush"] = "#167550", ["AccentTextBrush"] = "#FFFFFF" }
            : new Dictionary<string, string> { ["BackgroundBrush"] = "#101318", ["SidebarBrush"] = "#14181F", ["SurfaceBrush"] = "#1A2029", ["HoverBrush"] = "#26313C", ["BorderBrush"] = "#2C3541", ["TextBrush"] = "#F0F3F6", ["MutedBrush"] = "#9FAEBE", ["AccentBrush"] = "#99E3C6", ["AccentTextBrush"] = "#112B22" };
        foreach (var pair in colors) Resources[pair.Key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(pair.Value));
    }
    public async void ExitApplication()
    {
        if (exiting) return; exiting = true; hotkeys?.Dispose(); controller?.Dispose(); if (vm is not null) await vm.StopAsync(); overlays?.Dispose();
        if (trayIcon is not null) { trayIcon.Visible = false; var icon = trayIcon.Icon; var menu = trayIcon.ContextMenuStrip; trayIcon.Dispose(); icon?.Dispose(); menu?.Dispose(); }
        if (main is not null) { main.AllowClose = true; main.Close(); }
        activationWait?.Unregister(null); activation?.Dispose();
        if (host is not null) { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3)); try { await host.StopAsync(timeout.Token); } catch (OperationCanceledException) { } host.Dispose(); }
        singleInstance?.Dispose(); Shutdown();
    }
}
