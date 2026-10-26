using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using GaameTranslate.Core;

namespace GaameTranslate.Windows;

/// <summary>One native window per monitor. Pointer and drag state always remain in physical pixels.</summary>
public sealed class RegionSelector
{
    private readonly List<SelectionWindow> windows = [];
    private TaskCompletionSource<PixelRect?>? completion;
    private NativeMethods.Point? start;
    public bool IsSelecting => completion is not null;
    public async Task<PixelRect?> SelectAsync(CancellationToken token)
    {
        if (completion is not null) return null;
        completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler displayChanged = (_, _) => System.Windows.Application.Current.Dispatcher.BeginInvoke(() => Complete(null));
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += displayChanged;
        System.ComponentModel.PropertyChangedEventHandler languageChanged = (_, _) => { foreach (var window in windows) window.visual.InvalidateVisual(); };
        L.State.PropertyChanged += languageChanged;
        using var registration = token.Register(() => System.Windows.Application.Current.Dispatcher.BeginInvoke(() => Complete(null)));
        try
        {
            foreach (var display in DesktopCapture.Displays) windows.Add(new SelectionWindow(this, display));
            foreach (var window in windows) window.Show();
            windows.FirstOrDefault(w => w.Display.Index == DesktopCapture.CursorDisplay().Index)?.Activate();
            return await completion.Task;
        }
        finally
        {
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= displayChanged;
            L.State.PropertyChanged -= languageChanged;
            foreach (var window in windows) window.Close(); windows.Clear(); start = null; completion = null;
        }
    }
    private void Complete(PixelRect? bounds) => completion?.TrySetResult(bounds is { IsValid: true } ? bounds : null);
    private PixelRect? Current()
    {
        if (start is not { } first) return null;
        NativeMethods.GetCursorPos(out var last);
        return new PixelRect(Math.Min(first.X, last.X), Math.Min(first.Y, last.Y), Math.Abs(last.X - first.X), Math.Abs(last.Y - first.Y));
    }
    private sealed class SelectionWindow : Window
    {
        private readonly RegionSelector owner;
        internal readonly SelectionVisual visual;
        public DisplayInfo Display { get; }
        public SelectionWindow(RegionSelector selector, DisplayInfo display)
        {
            owner = selector; Display = display;
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true;
            Background = Brushes.Transparent; Topmost = true; ShowInTaskbar = false; Cursor = Cursors.Cross;
            Left = display.Bounds.X; Top = display.Bounds.Y; Width = display.Bounds.Width; Height = display.Bounds.Height;
            visual = new SelectionVisual(); Content = visual;
            SourceInitialized += (_, _) =>
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                NativeMethods.SetWindowPos(hwnd, new IntPtr(-1), (int)display.Bounds.X, (int)display.Bounds.Y, (int)display.Bounds.Width, (int)display.Bounds.Height, 0x40);
                NativeMethods.SetWindowDisplayAffinity(hwnd, 0x11);
            };
            PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; owner.Complete(null); } };
            MouseLeftButtonDown += (_, e) => { NativeMethods.GetCursorPos(out var p); owner.start = p; CaptureMouse(); UpdateAll(); e.Handled = true; };
            MouseMove += (_, _) => { if (owner.start is not null) UpdateAll(); };
            MouseLeftButtonUp += (_, e) => { if (owner.start is null) return; var bounds = owner.Current(); ReleaseMouseCapture(); owner.Complete(bounds); e.Handled = true; };
            MouseRightButtonDown += (_, _) => owner.Complete(null);
        }
        private void UpdateAll()
        {
            var selected = owner.Current();
            foreach (var window in owner.windows)
            {
                var transform = PresentationSource.FromVisual(window)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
                var r = selected?.Intersect(window.Display.Bounds);
                window.visual.Selection = r is { IsValid: true } ? new Rect((r.Value.X - window.Display.Bounds.X) * transform.M11, (r.Value.Y - window.Display.Bounds.Y) * transform.M22, r.Value.Width * transform.M11, r.Value.Height * transform.M22) : Rect.Empty;
                window.visual.Dimensions = selected is { } s ? $"{(int)s.Width} × {(int)s.Height}" : "";
                window.visual.InvalidateVisual();
            }
        }
    }
    internal sealed class SelectionVisual : FrameworkElement
    {
        public Rect Selection { get; set; } = Rect.Empty;
        public string Dimensions { get; set; } = "";
        protected override void OnRender(DrawingContext dc)
        {
            var full = new RectangleGeometry(new Rect(RenderSize));
            Geometry dim = Selection.IsEmpty ? full : new CombinedGeometry(GeometryCombineMode.Exclude, full, new RectangleGeometry(Selection));
            dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(100, 4, 8, 14)), null, dim);
            if (!Selection.IsEmpty)
            {
                dc.DrawRectangle(Brushes.Transparent, new Pen(new SolidColorBrush(Color.FromRgb(126, 221, 190)), 1.5), Selection);
                DrawText(dc, Dimensions, Math.Clamp(Selection.Left, 12, Math.Max(12, ActualWidth - 180)), Math.Clamp(Selection.Bottom + 10, 10, Math.Max(10, ActualHeight - 42)));
            }
            DrawText(dc, L.Get("Drag to translate   ·   Esc to cancel"), 28, 28);
        }
        private void DrawText(DrawingContext dc, string text, double x, double y)
        {
            var formatted = new FormattedText(text, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 14, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(22, 28, 37)), null, new Rect(x - 10, y - 7, formatted.Width + 20, formatted.Height + 14), 8, 8);
            dc.DrawText(formatted, new System.Windows.Point(x, y));
        }
    }
}
