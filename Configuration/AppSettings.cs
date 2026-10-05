namespace ShowroomBot.Configuration;

public sealed class AppSettings
{
    public bool AutoStartDemo { get; set; }
    public int IdleMinutes { get; set; } = 10;
    public VpnSettings Vpn { get; set; } = new();
    public RdpSettings Rdp { get; set; } = new();
    public AutomationSettings Automation { get; set; } = new();
    public TelegramSettings Telegram { get; set; } = new();
}

public sealed class TelegramSettings
{
    public bool Enabled { get; set; }
    public bool NotifyDemoEvents { get; set; } = true;
    public string BotToken { get; set; } = string.Empty;
    public long AllowedUserId { get; set; }
}

public sealed class VpnSettings
{
    public string[] ConnectionNames { get; set; } = ["My VPN"];
    public int CheckIntervalSeconds { get; set; } = 5;
}

public sealed class RdpSettings
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 3389;
    public string UserName { get; set; } = string.Empty;
    public int CheckIntervalSeconds { get; set; } = 5;
    public int ConnectTimeoutSeconds { get; set; } = 2;
}

public sealed class AutomationSettings
{
    public MouseSettings Mouse { get; set; } = new();
    public TypingSettings Typing { get; set; } = new();
}

public sealed class MouseSettings
{
    public int MovementDurationMilliseconds { get; set; } = 300;
    public int StepDelayMilliseconds { get; set; } = 10;
    public double CurvatureRatio { get; set; } = 0.08;
    public double DeviationPixels { get; set; } = 2;
    public double DurationVariation { get; set; } = 0.25;
}

public sealed class TypingSettings
{
    public int MinimumDelayMilliseconds { get; set; } = 80;
    public double SlowdownFactor { get; set; } = 2;
    public int TempoSegmentCharacters { get; set; } = 24;
    public int JitterMilliseconds { get; set; } = 15;
    public double PauseProbability { get; set; } = 0.008;
    public int PauseMinimumMilliseconds { get; set; } = 800;
    public int PauseMaximumMilliseconds { get; set; } = 1200;
}
