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
    private bool exiting, shortcutsPaused, restoreMain, startupBalloonActive;
    private Task ocrWarmup = Task.CompletedTask;
    private readonly CancellationTokenSource ocrWarmupLifetime = new();
    private readonly Dictionary<Forms.ToolStripItem, UiMessage> trayLabels = [];
    public static string DataDirectory { get; private set; } = "";
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        L.SetLanguage(System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru" ? "ru" : "en");
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
            await host.StartAsync(); vm = host.Services.GetRequiredService<MainViewModel>(); await vm.InitializeAsync(); L.SetLanguage(vm.Settings.InterfaceLanguage); ApplyTheme(vm.Settings.LightTheme);
            main = new MainWindow(vm); MainWindow = main; overlays = host.Services.GetRequiredService<OverlayManager>(); var selector = new RegionSelector(); vm.SelectRegion = selector.SelectAsync;
            vm.SessionReady += overlays.Show; vm.ToggleOverlayRequested += overlays.Toggle; vm.InteractionRequested += overlays.ToggleInteraction;
            vm.WordLookupRequested += overlays.Lookup;
            vm.IsScreenOverlayVisible = () => overlays.IsVisible; vm.CloseOverlayRequested += overlays.Close;
            vm.OperationFailed += message => { if (main is { IsVisible: false }) { startupBalloonActive = false; trayIcon?.ShowBalloonTip(5000, "GaameTranslate", message, Forms.ToolTipIcon.Warning); } };
            vm.CaptureVisibilityChanged += hidden =>
            {
                if (hidden) { restoreMain = main.IsVisible; main.Hide(); overlays.HideForCapture(); }
                else if (!exiting) { if (restoreMain) { main.ShowActivated = false; main.Show(); main.ShowActivated = true; } overlays.RestoreAfterCapture(); }
            };
            hotkeys = new GlobalHotkeys(); hotkeys.Invoked += OnHotkey;
            controller = new ControllerShortcuts(); controller.Invoked += OnHotkey; controller.SetEnabled(vm.Settings.ControllerEnabled);
            vm.ShortcutRecordingChanged += recording =>
            {
                if (exiting) return;
                if (recording) hotkeys.Unregister(); else RegisterHotkeys();
                controller.SetEnabled(!recording && !shortcutsPaused && vm.Settings.ControllerEnabled);
            };
            vm.SettingsChanged += () => { RegisterHotkeys(); controller.SetEnabled(!shortcutsPaused && !vm.IsRecordingShortcut && vm.Settings.ControllerEnabled); ApplyTheme(vm.Settings.LightTheme); NativeMethods.StyleWindow(new WindowInteropHelper(main).EnsureHandle(), !vm.Settings.LightTheme); if (!vm.InterfaceOnlySettingsChange && vm.LastSession is { CaptureType: not "Text" } last) overlays.Show(last); };
            CreateTray(); RegisterHotkeys(); if (e.Args.Contains("--settings")) vm.PageIndex = 5; else if (e.Args.Contains("--vocabulary")) vm.PageIndex = 2;
            if (!vm.Settings.StartMinimized || e.Args.Contains("--show") || e.Args.Contains("--settings") || e.Args.Contains("--vocabulary")) main.Show();
            ocrWarmup = WarmOcrAsync();
            if (!e.Args.Contains("--smoke-test"))
            {
                vm.NotifyStartup();
                if (vm.IsStartupTipVisible && !main.IsVisible) { startupBalloonActive = true; trayIcon?.ShowBalloonTip(6000, L.Get("Dictionary mode is ready"), L.Get("No full translator is configured. English–Russian dictionary hints are available offline. Click for the quick start guide."), Forms.ToolTipIcon.Info); }
            }
            if (e.Args.Contains("--smoke-test"))
            {
                // Real native OCR, GDI and DPAPI diagnostic. Does not contact translation services.
                var report = await NativeSmokeTest.RunAsync(host.Services.GetRequiredService<IOcrProvider>(), host.Services.GetRequiredService<IScreenCapture>(), host.Services.GetRequiredService<ICredentialStore>());
                await File.WriteAllTextAsync(Path.Combine(DataDirectory, "native-smoke.json"), report); ExitApplication();
            }
        }
        catch (Exception error)
        {
            System.Windows.MessageBox.Show(error is UserFacingException ? error.Message : L.Format("GaameTranslate could not start. Check access to its data folder and try again.\n\n{0}", error.GetType().Name), "GaameTranslate", MessageBoxButton.OK, MessageBoxImage.Error); ExitApplication();
        }
    }
    private static string ReadDataDirectory(string[] args)
    {
        var index = Array.IndexOf(args, "--data-dir");
        return index >= 0 && index + 1 < args.Length ? Path.GetFullPath(args[index + 1]) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GaameTranslate");
    }
    private async Task WarmOcrAsync()
    {
        if (host?.Services.GetRequiredService<IOcrProvider>() is not AdaptiveOcrProvider adaptive || vm is null) return;
        try
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            await adaptive.WarmUpAsync(vm.Settings.OcrEngine, vm.Settings.OcrUseGpu, vm.Settings.OcrLanguage, ocrWarmupLifetime.Token);
            host.Services.GetRequiredService<ILogger<App>>().LogInformation("OCR background preparation completed in {ElapsedMs:0} ms", watch.Elapsed.TotalMilliseconds);
        }
        catch (OperationCanceledException) when (ocrWarmupLifetime.IsCancellationRequested) { }
        catch (Exception error) { host.Services.GetRequiredService<ILogger<App>>().LogWarning("OCR preparation failed: {ExceptionType}", error.GetType().Name); }
    }
    private void RegisterHotkeys()
    {
        if (shortcutsPaused || hotkeys is null || vm is null || vm.IsRecordingShortcut) return;
        var conflicts = hotkeys.Register(vm.Settings.Hotkeys);
        if (conflicts.Count > 0) vm.Status = L.M("Сочетания заняты: {0}. Выберите другие на вкладке «Клавиши».", string.Join(", ", conflicts.Select(c => c[(c.IndexOf(':') + 1)..].Trim())));
    }
    private async void OnHotkey(string action)
    {
        if (vm is null || overlays is null || vm.IsRecordingShortcut) return;
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
        trayIcon = new Forms.NotifyIcon { Text = L.Get("GaameTranslate · ready"), Visible = true };
        using var process = System.Diagnostics.Process.GetCurrentProcess(); trayIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(process.MainModule!.FileName!) ?? System.Drawing.SystemIcons.Application;
        var menu = new Forms.ContextMenuStrip();
        Forms.ToolStripItem Item(string key, EventHandler handler) { var item = menu.Items.Add(L.Get(key), null, handler); trayLabels[item] = new(key); return item; }
        Item("Open GaameTranslate", (_, _) => ShowMain()); Item("Translate a region", (_, _) => OnHotkey("Region")); Item("Translate fullscreen", (_, _) => OnHotkey("Fullscreen")); Item("Repeat last region", (_, _) => OnHotkey("Repeat")); Item("Vocabulary", (_, _) => OnHotkey("Vocabulary"));
        Item("Quick start guide", (_, _) => vm?.OpenQuickStartCommand.Execute(null));
        trayIcon.BalloonTipClicked += (_, _) => { if (startupBalloonActive && vm?.IsStartupTipVisible == true) vm.OpenQuickStartCommand.Execute(null); else ShowMain(); startupBalloonActive = false; };
        Item("Show / hide overlay", (_, _) => overlays?.Toggle()); Item("Toggle overlay interaction", (_, _) => overlays?.ToggleInteraction()); menu.Items.Add(new Forms.ToolStripSeparator());
        Item("Экранный переводчик", (_, _) => OnHotkey("ScreenTranslation")); Item("Слово под курсором", (_, _) => OnHotkey("WordLookup"));
        var pause = Item("Pause shortcuts", (_, _) => { }); pause.Click += (_, _) => { shortcutsPaused = !shortcutsPaused; trayLabels[pause] = new(shortcutsPaused ? "Resume shortcuts" : "Pause shortcuts"); RefreshTrayLanguage(null, null); controller?.SetEnabled(!shortcutsPaused && vm?.IsRecordingShortcut == false && vm.Settings.ControllerEnabled); if (shortcutsPaused) { hotkeys?.Unregister(); vm?.StopMonitoring(); } else RegisterHotkeys(); };
        Item("Exit", (_, _) => ExitApplication()); trayIcon.ContextMenuStrip = menu; trayIcon.DoubleClick += (_, _) => ShowMain();
        L.State.PropertyChanged += RefreshTrayLanguage;
    }
    private void RefreshTrayLanguage(object? sender, System.ComponentModel.PropertyChangedEventArgs? e)
    {
        if (trayIcon is not null) trayIcon.Text = L.Get("GaameTranslate · ready");
        foreach (var item in trayLabels) item.Key.Text = item.Value.Text;
    }
    public void ShowMain() { if (main is null) return; main.Show(); main.WindowState = WindowState.Normal; main.Activate(); }
    public static void ApplyTheme(bool light)
    {
        var colors = light ? new Dictionary<string, string> { ["BackgroundBrush"] = "#F4F6F8", ["SidebarBrush"] = "#ECEFF3", ["SurfaceBrush"] = "#FFFFFF", ["HoverBrush"] = "#E3EBE8", ["BorderBrush"] = "#CDD5DD", ["TextBrush"] = "#17232D", ["MutedBrush"] = "#526471", ["AccentBrush"] = "#167550", ["AccentTextBrush"] = "#FFFFFF" }
            : new Dictionary<string, string> { ["BackgroundBrush"] = "#101318", ["SidebarBrush"] = "#14181F", ["SurfaceBrush"] = "#1A2029", ["HoverBrush"] = "#26313C", ["BorderBrush"] = "#2C3541", ["TextBrush"] = "#F0F3F6", ["MutedBrush"] = "#9FAEBE", ["AccentBrush"] = "#99E3C6", ["AccentTextBrush"] = "#112B22" };
        foreach (var pair in colors) Current.Resources[pair.Key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(pair.Value));
    }
    public async void ExitApplication()
    {
        if (exiting) return; exiting = true; L.State.PropertyChanged -= RefreshTrayLanguage; hotkeys?.Dispose(); controller?.Dispose(); ocrWarmupLifetime.Cancel(); if (vm is not null) await vm.StopAsync(); await ocrWarmup; overlays?.Dispose(); ocrWarmupLifetime.Dispose();
        if (trayIcon is not null) { trayIcon.Visible = false; var icon = trayIcon.Icon; var menu = trayIcon.ContextMenuStrip; trayIcon.Dispose(); icon?.Dispose(); menu?.Dispose(); }
        if (main is not null) { main.AllowClose = true; main.Close(); }
        activationWait?.Unregister(null); activation?.Dispose();
        if (host is not null) { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3)); try { await host.StopAsync(timeout.Token); } catch (OperationCanceledException) { } host.Dispose(); }
        singleInstance?.Dispose(); Shutdown();
    }
}
