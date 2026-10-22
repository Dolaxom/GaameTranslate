using System.Windows.Input;
using System.Windows.Interop;
using GaameTranslate.Core;

namespace GaameTranslate.Windows;

public sealed class GlobalHotkeys : IDisposable
{
    private readonly HwndSource source = new(new HwndSourceParameters("GaameTranslate.Hotkeys") { ParentWindow = new IntPtr(-3) });
    private readonly Dictionary<int, string> registered = new();
    public event Action<string>? Invoked;
    public GlobalHotkeys() => source.AddHook(Hook);
    public static (uint Modifiers, uint Key) Parse(string shortcut)
    {
        var parts = shortcut.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) throw new UserFacingException("A shortcut needs Ctrl, Alt, Shift or Win plus a letter, number or function key.");
        uint modifiers = 0;
        foreach (var part in parts[..^1]) modifiers |= part.ToLowerInvariant() switch
        { "ctrl" => 2u, "shift" => 4u, "alt" => 1u, "win" => 8u, _ => throw new UserFacingException($"Unknown shortcut modifier '{part}'.") };
        if (!Enum.TryParse<Key>(parts[^1].Length == 1 && char.IsDigit(parts[^1][0]) ? "D" + parts[^1] : parts[^1], true, out var key)
            || !((key >= Key.A && key <= Key.Z) || (key >= Key.D0 && key <= Key.D9) || (key >= Key.F1 && key <= Key.F24)))
            throw new UserFacingException("Use a letter, number or F1–F24 as the shortcut key.");
        return (modifiers | 0x4000u, (uint)KeyInterop.VirtualKeyFromKey(key)); // MOD_NOREPEAT
    }
    public static IReadOnlyList<(string Action, string Shortcut)> Bindings(HotkeySettings settings) => [
        ("Region", settings.Region), ("Fullscreen", settings.Fullscreen), ("Repeat", settings.Repeat),
        ("ToggleOverlay", settings.ToggleOverlay), ("Vocabulary", settings.Vocabulary), ("Interaction", settings.Interaction)];
    public static void Validate(HotkeySettings settings)
    {
        var parsed = Bindings(settings).Select(b => Parse(b.Shortcut)).ToArray();
        if (parsed.Distinct().Count() != parsed.Length) throw new UserFacingException("Each action needs a different shortcut.");
    }
    public IReadOnlyList<string> Register(HotkeySettings settings)
    {
        Validate(settings); Unregister(); var conflicts = new List<string>(); var id = 100;
        foreach (var binding in Bindings(settings))
        {
            var (modifiers, key) = Parse(binding.Shortcut);
            if (NativeMethods.RegisterHotKey(source.Handle, id, modifiers, key)) registered[id] = binding.Action;
            else conflicts.Add($"{binding.Action}: {binding.Shortcut}");
            id++;
        }
        return conflicts;
    }
    private IntPtr Hook(IntPtr hwnd, int message, IntPtr wparam, IntPtr lparam, ref bool handled)
    {
        if (message == 0x312 && registered.TryGetValue(wparam.ToInt32(), out var action)) { handled = true; Invoked?.Invoke(action); }
        return IntPtr.Zero;
    }
    public void Unregister() { foreach (var id in registered.Keys) NativeMethods.UnregisterHotKey(source.Handle, id); registered.Clear(); }
    public void Dispose() { Unregister(); source.RemoveHook(Hook); source.Dispose(); }
}
