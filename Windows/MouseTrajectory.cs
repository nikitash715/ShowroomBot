using ShowroomBot.Configuration;

namespace ShowroomBot.Windows;

public sealed class MouseTrajectory
{
    private readonly Point _start, _target;
    private readonly double _normalX, _normalY, _bend, _deviation, _phase;
    public MouseTrajectory(Point start, Point target, MouseSettings settings, Random? random = null)
    {
        var rng = random ?? Random.Shared;
        _start = start; _target = target;
        var dx = (double)target.X - start.X;
        var dy = (double)target.Y - start.Y;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        _normalX = distance == 0 ? 0 : -dy / distance;
        _normalY = distance == 0 ? 0 : dx / distance;
        _bend = distance * settings.CurvatureRatio * (rng.NextDouble() * 2 - 1);
        _deviation = Math.Min(settings.DeviationPixels, distance * 0.02);
        _phase = rng.NextDouble() * Math.PI * 2;
    }
    public Point At(double progress)
    {
        if (progress <= 0) return _start;
        if (progress >= 1) return _target;
        // Smoothstep provides continuous acceleration and deceleration.
        var t = progress * progress * (3 - 2 * progress);
        var offset = Math.Sin(Math.PI * t) * (_bend + _deviation * Math.Sin(4 * Math.PI * t + _phase));
        return new Point((int)Math.Round(_start.X + (_target.X - _start.X) * t + _normalX * offset),
            (int)Math.Round(_start.Y + (_target.Y - _start.Y) * t + _normalY * offset));
    }
}
