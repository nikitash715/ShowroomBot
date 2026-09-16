using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace ShowroomBot.Windows;

public sealed class VpnDetector
{
    public VpnStatus GetStatus(IEnumerable<string> connectionNames)
    {
        var expectedNames = connectionNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (expectedNames.Count == 0)
        {
            return VpnStatus.Disconnected;
        }

        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up ||
                !expectedNames.Contains(networkInterface.Name))
            {
                continue;
            }

            var hasIpv4 = networkInterface
                .GetIPProperties()
                .UnicastAddresses
                .Any(address => address.Address.AddressFamily == AddressFamily.InterNetwork);

            if (hasIpv4)
            {
                return new VpnStatus(true, networkInterface.Name);
            }
        }

        return VpnStatus.Disconnected;
    }
}
