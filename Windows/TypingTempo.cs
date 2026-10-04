using ShowroomBot.Configuration;

namespace ShowroomBot.Windows;

public sealed class TypingTempo(TypingSettings settings, Random? random = null)
{
    private readonly Random _random = random ?? Random.Shared;
    private int _position;
    private double _from = 1;
    private double _to = 1;

    public double NextDelay()
    {
        if (_position == 0)
            _to = 1 + _random.NextDouble() * (settings.SlowdownFactor - 1);
        var t = (double)_position / settings.TempoSegmentCharacters;
        var blend = t * t * (3 - 2 * t);
        var delay = settings.MinimumDelayMilliseconds * (_from + (_to - _from) * blend)
            + _random.NextDouble() * settings.JitterMilliseconds;
        if (++_position >= settings.TempoSegmentCharacters) { _position = 0; _from = _to; }
        if (_random.NextDouble() < settings.PauseProbability)
            delay += settings.PauseMinimumMilliseconds + _random.NextDouble() *
                (settings.PauseMaximumMilliseconds - settings.PauseMinimumMilliseconds);
        return Math.Max(settings.MinimumDelayMilliseconds, delay);
    }
}
