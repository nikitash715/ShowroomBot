namespace ShowroomBot.Core;

public sealed class IdleAutoStartTimer
{
    private readonly TimeProvider _timeProvider;
    private long? _lastCompletion;

    public IdleAutoStartTimer(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public bool ShouldStart(bool enabled, TimeSpan idleTime, TimeSpan threshold,
        bool isRunning, bool infrastructureReady)
    {
        return enabled && !isRunning && infrastructureReady && idleTime >= threshold &&
            (!_lastCompletion.HasValue ||
             _timeProvider.GetElapsedTime(_lastCompletion.Value) >= threshold);
    }

    public void RestartInterval()
    {
        _lastCompletion = _timeProvider.GetTimestamp();
    }
}
