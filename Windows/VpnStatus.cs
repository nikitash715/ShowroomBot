namespace ShowroomBot.Windows;

public sealed class VpnStatus
{
    public static VpnStatus Disconnected { get; } = new(false, null);

    public VpnStatus(bool isConnected, string? activeConnectionName)
    {
        IsConnected = isConnected;
        ActiveConnectionName = activeConnectionName;
    }

    public bool IsConnected { get; }
    public string? ActiveConnectionName { get; }
}
