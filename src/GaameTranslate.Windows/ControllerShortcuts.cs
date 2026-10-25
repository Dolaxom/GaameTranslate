using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace GaameTranslate.Windows;

/// <summary>Opt-in XInput polling. Reads button states only, never injects input into games.</summary>
public sealed class ControllerShortcuts : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] private struct State { public uint Packet; public ushort Buttons; public byte LeftTrigger, RightTrigger; public short LX, LY, RX, RY; }
    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")] private static extern uint GetState(uint index, out State state);
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly ushort[] previous = new ushort[4];
    public event Action<string>? Invoked;
    public ControllerShortcuts() { timer.Tick += Poll; }
    public void SetEnabled(bool enabled) { Array.Clear(previous); if (enabled) timer.Start(); else timer.Stop(); }
    private void Poll(object? sender, EventArgs e)
    {
        for (uint index = 0; index < 4; index++)
        {
            if (GetState(index, out var state) != 0) { previous[index] = 0; continue; }
            var buttons = state.Buttons;
            // Hold both shoulders, then Y = screen translation, X = region repeat, B = close overlay.
            if ((buttons & 0x300) == 0x300) { var pressed = (ushort)(buttons & ~previous[index]); if ((pressed & 0x8000) != 0) Invoked?.Invoke("ScreenTranslation"); else if ((pressed & 0x4000) != 0) Invoked?.Invoke("Repeat"); else if ((pressed & 0x2000) != 0) Invoked?.Invoke("ToggleOverlay"); }
            previous[index] = buttons;
        }
    }
    public void Dispose() { timer.Stop(); timer.Tick -= Poll; }
}
