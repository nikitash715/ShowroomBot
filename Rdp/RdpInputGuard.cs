using ShowroomBot.Core.Scenarios;
using ShowroomBot.Windows;

namespace ShowroomBot.Rdp;

internal sealed class RdpInputGuard : IDisposable
{
    private static readonly AsyncLocal<IntPtr?> RequiredWindow = new();
    private readonly IntPtr? _previous;
    private static readonly AsyncLocal<bool> Strict = new();
    private readonly bool _previousStrict;

    private RdpInputGuard(IntPtr handle, bool strict = false)
    {
        _previous = RequiredWindow.Value;
        _previousStrict = Strict.Value;
        RequiredWindow.Value = handle;
        Strict.Value = strict;
    }

    public static IDisposable Require(IntPtr handle) => new RdpInputGuard(handle);
    public static IDisposable RequireStrict(IntPtr handle) => new RdpInputGuard(handle, true);

    public static void CheckRelease()
    {
        if (Strict.Value) Check();
    }

    public static Point ConstrainPointer(Point point)
    {
        if (!Strict.Value) return point;
        Check();
        var handle = RequiredWindow.Value!.Value;
        if (!NativeMethods.GetClientRect(handle, out var client)) throw new InvalidOperationException("ReadCode: нет границ RDP.");
        var origin = new NativeMethods.POINT();
        if (!NativeMethods.ClientToScreen(handle, ref origin)) throw new InvalidOperationException("ReadCode: нет координат RDP.");
        // Keep the entire curved trajectory inside the remote client, even when it starts locally.
        return new Point(Math.Clamp(point.X, origin.X + 2, origin.X + client.Right - 3),
            Math.Clamp(point.Y, origin.Y + 2, origin.Y + client.Bottom - 3));
    }

    public static void CheckPointer(Point? target = null)
    {
        if (!Strict.Value) return;
        Check();
        if (!NativeMethods.GetCursorPos(out var cursor)) throw new InvalidOperationException("ReadCode: нет позиции мыши.");
        var point = target ?? new Point(cursor.X, cursor.Y);
        if (ConstrainPointer(point) != point) throw new InvalidOperationException("ReadCode: мышь вне RDP.");
        var atPoint = NativeMethods.WindowFromPoint(new NativeMethods.POINT { X = point.X, Y = point.Y });
        if (NativeMethods.GetAncestor(atPoint, 2) != RequiredWindow.Value)
            throw new InvalidOperationException("ReadCode: точка RDP перекрыта локальным окном.");
    }

    public static void Check()
    {
        // Never let a previous remote step impose an RDP foreground check on local input.
        if (!Strict.Value && ScenarioExecution.Current?.ExecutionContext == ScenarioExecutionContext.Local) return;
        var handle = RequiredWindow.Value ?? ScenarioExecution.Current?.RdpWindow;
        if (handle.HasValue) RdpController.EnsureSessionForeground(handle.Value);
    }

    public void Dispose()
    {
        RequiredWindow.Value = _previous;
        Strict.Value = _previousStrict;
    }
}
