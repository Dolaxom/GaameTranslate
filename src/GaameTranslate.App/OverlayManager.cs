using System.Diagnostics;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using GaameTranslate.Core;
using GaameTranslate.Windows;
using Microsoft.Extensions.Logging;

namespace GaameTranslate.App;

public sealed class OverlayManager(MainViewModel vm, ILogger<OverlayManager> logger) : IDisposable
{
    private readonly List<Window> windows = [];
    private AppSettings preferences = vm.Settings;
    private bool interactive = true;
    private bool visible = true;
    private WordLookupWindow? lookup;
    public bool IsVisible => visible && windows.Count > 0;
    public void Show(TranslationSession session)
    {
        if (string.IsNullOrWhiteSpace(session.TranslatedText)) return;
        Close(); preferences = vm.SettingsForSession(session); visible = true; interactive = !preferences.ClickThrough; var watch = Stopwatch.StartNew();
        if (session.CaptureType is "Screen translation" or "Watch region") ShowSpatial(session, true);
        else if (preferences.OverlayMode is OverlayMode.Floating or OverlayMode.SidePanel) ShowFloating(session);
        else ShowSpatial(session, preferences.OverlayMode == OverlayMode.Replacement);
        logger.LogInformation("Overlay rendered in {ElapsedMs:0} ms", watch.Elapsed.TotalMilliseconds);
    }
    private SolidColorBrush Surface()
    {
        var color = preferences.LightTheme ? Color.FromRgb(244, 247, 249) : Color.FromRgb(23, 29, 38); color.A = (byte)(preferences.OverlayOpacity * 255); return new(color);
    }
    private Window BaseWindow(string title)
    {
        var window = Ui.Localize(new Window { Title = title, WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, ResizeMode = ResizeMode.NoResize, ShowActivated = false, ShowInTaskbar = false, Topmost = true });
        window.SetResourceReference(FrameworkElement.StyleProperty, typeof(Window));
        window.SourceInitialized += (_, _) => { var hwnd = new WindowInteropHelper(window).Handle; NativeMethods.SetWindowDisplayAffinity(hwnd, 0x11); NativeMethods.ClickThrough(hwnd, !interactive); };
        window.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
        windows.Add(window); return window;
    }
    private static Button SmallButton(string title, Action action)
    {
        var button = Ui.Localize(new Button { Content = title, Padding = new(9, 5, 9, 5), Margin = new(5, 0, 0, 0), FontSize = 11, MinHeight = 28 });
        button.Click += (_, _) => action(); return button;
    }
    private FrameworkElement Block(TranslatedBlock block, TranslationSession session, bool compact = false)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = block.Translation, FontSize = preferences.OverlayFontSize, LineHeight = preferences.OverlayFontSize * 1.45, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 9) });
        if (!compact)
        {
            var source = new TextBox { Text = block.Original.Text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new(0), Background = Brushes.Transparent, Padding = new(0), FontSize = 13, MinHeight = 0, Margin = new(0, 0, 0, 8) };
            source.SetResourceReference(Control.ForegroundProperty, "MutedBrush");
            Ui.Set(source, FrameworkElement.ToolTipProperty, "Select text, then use Look up selection for custom Japanese/Chinese substrings."); panel.Children.Add(source);
            var words = new WrapPanel();
            foreach (var word in block.Original.Words)
            {
                var button = new Button { Content = word.Text, FontSize = 12, Padding = new(7, 4, 7, 4), Margin = new(0, 0, 5, 5), ToolTip = "Look up and save this recognized word" };
                Ui.Set(button, FrameworkElement.ToolTipProperty, "Look up and save this recognized word"); button.Click += (_, _) => Lookup(word.Text, session, block); words.Children.Add(button);
            }
            panel.Children.Add(words);
            panel.Children.Add(SmallButton("Look up selection", () => Lookup(string.IsNullOrWhiteSpace(source.SelectedText) ? source.Text : source.SelectedText, session, block)));
        }
        else
        {
            var original = new TextBlock { Text = block.Original.Text, FontSize = 11, MaxHeight = 36, TextTrimming = TextTrimming.CharacterEllipsis }; original.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); panel.Children.Add(original);
            panel.MouseLeftButtonUp += (_, e) => { Lookup(block.Original.Words.FirstOrDefault()?.Text ?? block.Original.Text, session, block); e.Handled = true; };
            Ui.Set(panel, FrameworkElement.ToolTipProperty, "Click to look up a word. Edit the selection in the dictionary popup.");
        }
        return panel;
    }
    private void ShowFloating(TranslationSession session)
    {
        var display = DisplayFor(session.CaptureBounds); var side = preferences.OverlayMode == OverlayMode.SidePanel;
        var window = BaseWindow("GaameTranslate · translation"); window.Width = side ? 390 : 470; window.MinWidth = 300; window.MinHeight = 180; window.Height = Math.Min(side ? 680 : 510, display.WorkArea.Height * 96 / 144); window.ResizeMode = ResizeMode.CanResizeWithGrip;
        var root = new Grid(); root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new()); root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var chrome = new DockPanel { Margin = new(16, 13, 12, 10), LastChildFill = true };
        var actions = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(actions, Dock.Right); chrome.Children.Add(actions);
        var pin = SmallButton("Unpin", () => { }); pin.Click += (_, _) => { window.Topmost = !window.Topmost; Ui.SetContent(pin, window.Topmost ? "Unpin" : "Pin"); }; actions.Children.Add(pin);
        actions.Children.Add(SmallButton("Copy", () => vm.Copy(session.TranslatedText))); actions.Children.Add(SmallButton("Close", Close));
        chrome.Children.Add(Ui.Localize(new TextBlock { Text = "GAAME  /  TRANSLATE", FontSize = 10, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)System.Windows.Application.Current.FindResource("AccentBrush") }));
        chrome.MouseLeftButtonDown += (_, e) => { if (e.OriginalSource is TextBlock or DockPanel) { try { window.DragMove(); } catch (InvalidOperationException) { } } }; root.Children.Add(chrome);
        var blocks = new StackPanel { Margin = new(18, 6, 18, 10) }; foreach (var block in session.Blocks)
        {
            var content = Block(block, session); content.Margin = new(0, 0, 0, 20); blocks.Children.Add(content);
        }
        var scroll = new ScrollViewer { Content = blocks, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; Grid.SetRow(scroll, 1); root.Children.Add(scroll);
        var footer = new DockPanel { Margin = new(18, 4, 18, 13) }; var interaction = SmallButton("Click through", () => SetInteractive(false)); DockPanel.SetDock(interaction, Dock.Right); footer.Children.Add(interaction);
        footer.Children.Add(SettingsView.Muted(L.M("{0} → {1}  ·  {2}", session.SourceLanguage, session.TargetLanguage, session.Provider), new(0, 5, 5, 0))); Grid.SetRow(footer, 2); root.Children.Add(footer);
        window.Content = new Border { Background = Surface(), BorderBrush = (Brush)System.Windows.Application.Current.FindResource("BorderBrush"), BorderThickness = new(1), CornerRadius = new(12), Child = root };
        if (!side) { root.Measure(new Size(window.Width, double.PositiveInfinity)); window.Height = Math.Clamp(root.DesiredSize.Height + 12, 180, 510); }
        window.Show(); var hwnd = new WindowInteropHelper(window).Handle; var dpi = NativeMethods.GetDpiForWindow(hwnd) / 96d;
        var width = Math.Min(display.WorkArea.Width - 24, window.Width * dpi); var height = Math.Min(display.WorkArea.Height - 24, window.Height * dpi);
        var x = side ? display.WorkArea.Right - width - 16 : Math.Clamp(session.CaptureBounds.X, display.WorkArea.X + 12, Math.Max(display.WorkArea.X + 12, display.WorkArea.Right - width - 12));
        var y = side ? display.WorkArea.Y + 16 : Math.Clamp(session.CaptureBounds.Bottom + 16, display.WorkArea.Y + 12, Math.Max(display.WorkArea.Y + 12, display.WorkArea.Bottom - height - 12));
        NativeMethods.SetWindowPos(hwnd, new IntPtr(-1), (int)x, (int)y, (int)width, (int)height, 0x10); Fade(window);
    }
    private static DisplayInfo DisplayFor(PixelRect bounds) => DesktopCapture.Displays.OrderByDescending(d => { var i = d.Bounds.Intersect(bounds); return i.Width * i.Height; }).First();
    private void ShowSpatial(TranslationSession session, bool replacement)
    {
        foreach (var display in DesktopCapture.Displays)
        {
            var relevant = session.Blocks.Where(b => DisplayFor(b.Original.Bounds).Index == display.Index).ToArray(); if (relevant.Length == 0) continue;
            var window = BaseWindow("GaameTranslate · spatial overlay"); var canvas = new Canvas { Background = null, ClipToBounds = true }; window.Content = canvas;
            window.Width = display.Bounds.Width; window.Height = display.Bounds.Height; window.Show(); var hwnd = new WindowInteropHelper(window).Handle;
            NativeMethods.SetWindowPos(hwnd, new IntPtr(-1), (int)display.Bounds.X, (int)display.Bounds.Y, (int)display.Bounds.Width, (int)display.Bounds.Height, 0x10);
            var transform = PresentationSource.FromVisual(window)!.CompositionTarget!.TransformFromDevice;
            var width = display.Bounds.Width * transform.M11; var height = display.Bounds.Height * transform.M22; var used = new List<Rect>();
            if (preferences.FreezeImage && vm.LastSession?.Id == session.Id && vm.LastFrame is { } frame)
            {
                var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(frame.Width, frame.Height, 96, 96, PixelFormats.Bgra32, null, frame.Bgra, frame.Width * 4);
                var frozen = new Image { Source = bitmap, Width = frame.Bounds.Width * transform.M11, Height = frame.Bounds.Height * transform.M22, IsHitTestVisible = false };
                Canvas.SetLeft(frozen, (frame.Bounds.X - display.Bounds.X) * transform.M11); Canvas.SetTop(frozen, (frame.Bounds.Y - display.Bounds.Y) * transform.M22); canvas.Children.Add(frozen);
            }
            foreach (var block in relevant)
            {
                var original = block.Original.Bounds; var left = (original.X - display.Bounds.X) * transform.M11; var top = (original.Y - display.Bounds.Y) * transform.M22;
                FrameworkElement content;
                if (replacement)
                {
                    var replacementCard = ReplacementCard(block, session, transform.M11, transform.M22);
                    var cardWidth = Math.Min(width, original.Width * transform.M11 + 12);
                    var cardHeight = Math.Min(height, original.Height * transform.M22 + 10);
                    replacementCard.Width = cardWidth; replacementCard.Height = cardHeight;
                    Canvas.SetLeft(replacementCard, Math.Clamp(left - 6, 0, Math.Max(0, width - cardWidth)));
                    Canvas.SetTop(replacementCard, Math.Clamp(top - 5, 0, Math.Max(0, height - cardHeight)));
                    canvas.Children.Add(replacementCard); continue;
                }
                else content = Block(block, session, true);
                var card = new Border { Background = Surface(), CornerRadius = new(7), Padding = new(replacement ? 7 : 12), Child = content, BorderBrush = (Brush)System.Windows.Application.Current.FindResource("BorderBrush"), BorderThickness = new(1), MaxWidth = Math.Max(80, Math.Min(replacement ? original.Width * transform.M11 + 14 : 340, width - 20)) };
                card.Measure(new Size(card.MaxWidth, double.PositiveInfinity)); var size = card.DesiredSize;
                left = Math.Clamp(left, 10, Math.Max(10, width - size.Width - 10)); top = Math.Clamp(replacement ? top : top + original.Height * transform.M22 + 5, 10, Math.Max(10, height - size.Height - 10));
                var rect = new Rect(left, top, size.Width, Math.Min(size.Height, height - 20));
                if (!replacement)
                {
                    var placed = OverlayPlacement.Place(new(rect.X, rect.Y, rect.Width, rect.Height), new(10, 10, width - 20, height - 20), used.Select(r => new PixelRect(r.X, r.Y, r.Width, r.Height)).ToArray());
                    if (placed is null) { Close(); ShowFloating(session); vm.Status = "The screen has too many overlapping labels. Switched to a readable floating panel."; return; }
                    rect = new Rect(placed.Value.X, placed.Value.Y, placed.Value.Width, placed.Value.Height);
                }
                card.MaxHeight = Math.Max(40, height - 20); Canvas.SetLeft(card, rect.X); Canvas.SetTop(card, Math.Clamp(rect.Y, 10, Math.Max(10, height - rect.Height - 10))); used.Add(rect); canvas.Children.Add(card);
            }
            if (replacement)
            {
                var selectWords = SmallButton("Выбрать слово / перевод", () =>
                {
                    foreach (var card in canvas.Children.OfType<Border>())
                        if (card.Tag is Action toggle) toggle();
                });
                Canvas.SetTop(selectWords, 12); Canvas.SetRight(selectWords, 145); canvas.Children.Add(selectWords);
            }
            var close = SmallButton("Закрыть", Close); Canvas.SetTop(close, 12); Canvas.SetRight(close, 18); canvas.Children.Add(close); Fade(window);
        }
    }
    private Border ReplacementCard(TranslatedBlock block, TranslationSession session, double sx, double sy)
    {
        var bounds = block.Original.Bounds;
        var background = block.BackgroundColor is { } hex ? (Color)ColorConverter.ConvertFromString(hex) : preferences.LightTheme ? Color.FromRgb(244, 247, 249) : Color.FromRgb(20, 23, 28);
        var light = (background.R * .2126 + background.G * .7152 + background.B * .0722) > 150;
        background.A = (byte)(Math.Max(.97, preferences.OverlayOpacity) * 255);
        var surface = new SolidColorBrush(background); var foreground = light ? Brushes.Black : Brushes.WhiteSmoke;
        var grid = new Grid(); var text = new TextBlock { Text = block.Translation, TextWrapping = TextWrapping.Wrap, Foreground = foreground, Padding = new(6, 5, 6, 5), FontFamily = new FontFamily("Segoe UI"), ToolTip = block.Translation + "\n\n" + block.Original.Text };
        var lineHeight = block.Original.LineBounds.Count > 0 ? block.Original.LineBounds.Select(r => r.Height).Order().ElementAt(block.Original.LineBounds.Count / 2) : bounds.Height;
        var initial = preferences.MatchSourceTypography ? Math.Clamp(lineHeight * sy * .78, 11, 40) : preferences.OverlayFontSize;
        text.FontSize = initial; var availableWidth = bounds.Width * sx + 12; var availableHeight = bounds.Height * sy + 10;
        while (text.FontSize > 9) { text.Measure(new Size(availableWidth, double.PositiveInfinity)); if (text.DesiredSize.Height <= availableHeight) break; text.FontSize -= .5; }
        grid.Children.Add(text);
        var words = new Canvas { Visibility = Visibility.Collapsed, Background = Brushes.Transparent };
        foreach (var word in block.Original.Words)
        {
            var button = new Border { Background = Brushes.Transparent, BorderBrush = Brushes.Transparent, BorderThickness = new(1), CornerRadius = new(2), Width = Math.Max(2, word.Bounds.Width * sx), Height = Math.Max(2, word.Bounds.Height * sy), ToolTip = word.Text, Cursor = System.Windows.Input.Cursors.Hand };
            button.MouseEnter += (_, _) => { button.Background = new SolidColorBrush(Color.FromArgb(100, 153, 227, 198)); button.BorderBrush = Brushes.MediumAquamarine; };
            button.MouseLeave += (_, _) => { button.Background = Brushes.Transparent; button.BorderBrush = Brushes.Transparent; };
            button.MouseLeftButtonUp += (_, e) => { Lookup(word.Text, session, block); e.Handled = true; };
            Canvas.SetLeft(button, (word.Bounds.X - bounds.X) * sx + 6); Canvas.SetTop(button, (word.Bounds.Y - bounds.Y) * sy + 5); words.Children.Add(button);
        }
        grid.Children.Add(words);
        var card = new Border { Background = surface, CornerRadius = new(5), Child = grid, ClipToBounds = true };
        var dictionaryMode = false;
        card.Tag = (Action)(() => { dictionaryMode = !dictionaryMode; words.Visibility = dictionaryMode ? Visibility.Visible : Visibility.Collapsed; text.Visibility = dictionaryMode ? Visibility.Collapsed : Visibility.Visible; card.Background = dictionaryMode ? Brushes.Transparent : surface; });
        card.PreviewMouseWheel += (_, e) => { text.FontSize = Math.Clamp(text.FontSize + (e.Delta > 0 ? 1 : -1), 9, 40); e.Handled = true; };
        return card;
    }
    private static void Fade(Window window) => window.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(110)));
    public void Lookup(string word, TranslationSession session, TranslatedBlock block)
    {
        lookup?.Close(); lookup = new WordLookupWindow(vm, word, session, block); lookup.Show(); lookup.Activate();
    }
    public void SetInteractive(bool enabled)
    {
        interactive = enabled; foreach (var w in windows) NativeMethods.ClickThrough(new WindowInteropHelper(w).Handle, !enabled);
        vm.Status = enabled ? "Overlay is interactive. Click a word to look it up." : L.M("Overlay is click-through. Press {0} to interact again.", preferences.Hotkeys.Interaction);
    }
    public void ToggleInteraction() => SetInteractive(!interactive);
    public void Toggle()
    {
        if (windows.Count == 0) { if (vm.LastSession is { CaptureType: not "Text" } session) Show(session); else vm.Status = "Capture some screen text first."; return; }
        visible = !visible; foreach (var window in windows) if (visible) window.Show(); else window.Hide();
    }
    public void HideForCapture() { lookup?.Close(); foreach (var window in windows) window.Hide(); }
    public void RestoreAfterCapture() { if (visible) foreach (var window in windows) window.Show(); }
    public void Close() { lookup?.Close(); lookup = null; foreach (var window in windows.ToArray()) window.Close(); windows.Clear(); }
    public void Dispose() => Close();
}
