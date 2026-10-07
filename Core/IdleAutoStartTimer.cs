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
        bool isRunning, bool infrastructureReady, TimeOnly? startTime = null, TimeOnly? endTime = null)
    {
        return enabled && (!startTime.HasValue || !endTime.HasValue ||
            IsWithinWindow(TimeOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime), startTime.Value, endTime.Value)) &&
            !isRunning && infrastructureReady && idleTime >= threshold &&
            (!_lastCompletion.HasValue ||
             _timeProvider.GetElapsedTime(_lastCompletion.Value) >= threshold);
    }

    public void RestartInterval()
    {
        _lastCompletion = _timeProvider.GetTimestamp();
    }

    public static bool IsWithinWindow(TimeOnly now, TimeOnly start, TimeOnly end) =>
        start < end ? now >= start && now < end : start > end && (now >= start || now < end);
}
