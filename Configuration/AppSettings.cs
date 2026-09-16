namespace ShowroomBot.Configuration;

public sealed class AppSettings
{
    public bool AutoStartDemo { get; set; } = true;
    public int IdleMinutes { get; set; } = 10;
    public VpnSettings Vpn { get; set; } = new();
    public RdpSettings Rdp { get; set; } = new();
}

public sealed class VpnSettings
{
    public string[] ConnectionNames { get; set; } = ["KLEVER VPN", "KLEVER VPN2"];
    public int CheckIntervalSeconds { get; set; } = 5;
}

public sealed class RdpSettings
{
    public string Host { get; set; } = "192.168.174.132";
    public int Port { get; set; } = 3389;
    public string UserName { get; set; } = "KLO06001@dpc.msmash.ru";
    public int CheckIntervalSeconds { get; set; } = 5;
    public int ConnectTimeoutSeconds { get; set; } = 2;
}
