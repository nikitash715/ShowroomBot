using System.Net.Sockets;

namespace ShowroomBot.Rdp;

public sealed class RdpAvailabilityChecker
{
    public async Task<bool> IsAvailableAsync(
        string host,
        int port,
        TimeSpan connectTimeout,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(host) || port <= 0 || port > 65535)
        {
            return false;
        }

        try
        {
            using var tcpClient = new TcpClient();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(connectTimeout);

            await tcpClient.ConnectAsync(host, port, timeoutCts.Token);
            return tcpClient.Connected;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (SocketException)
        {
            return false;
        }
    }
}
