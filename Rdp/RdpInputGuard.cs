using ShowroomBot.Core.Scenarios;

namespace ShowroomBot.Rdp;

internal sealed class RdpInputGuard : IDisposable
{
    private static readonly AsyncLocal<IntPtr?> RequiredWindow = new();
    private readonly IntPtr? _previous;

    private RdpInputGuard(IntPtr handle)
    {
        _previous = RequiredWindow.Value;
        RequiredWindow.Value = handle;
    }

    public static IDisposable Require(IntPtr handle) => new RdpInputGuard(handle);

    public static void Check()
    {
        var handle = RequiredWindow.Value ?? ScenarioExecution.Current?.RdpWindow;
        if (handle.HasValue) RdpController.EnsureSessionForeground(handle.Value);
    }

    public void Dispose() => RequiredWindow.Value = _previous;
}
