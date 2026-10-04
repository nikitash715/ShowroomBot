namespace ShowroomBot.Configuration;

public sealed class AppSettings
{
    public bool AutoStartDemo { get; set; }
    public int IdleMinutes { get; set; } = 10;
    public VpnSettings Vpn { get; set; } = new();
    public RdpSettings Rdp { get; set; } = new();
    public AutomationSettings Automation { get; set; } = new();
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
}

public sealed class MouseSettings
{
    public int MovementDurationMilliseconds { get; set; }
    public int StepDelayMilliseconds { get; set; }
}
