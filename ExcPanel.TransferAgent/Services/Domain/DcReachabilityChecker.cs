using System.Net.Sockets;
using ExcPanel.TransferAgent.Contracts;

namespace ExcPanel.TransferAgent.Services.Domain;

public static class DcReachabilityChecker
{
    private static readonly int[] DefaultDcPorts = [88, 389, 445];

    public static async Task<bool> IsReachableAsync(
        string? hostOrIp,
        ISafeProcessRunner processRunner,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(hostOrIp))
        {
            return false;
        }

        var icmp = await processRunner.RunAsync(
            "ping",
            ["-c", "1", "-W", "2", hostOrIp],
            timeoutSeconds: 5,
            cancellationToken: cancellationToken);

        if (icmp.ExitCode == 0)
        {
            return true;
        }

        foreach (var port in DefaultDcPorts)
        {
            if (await TryConnectTcpAsync(hostOrIp, port, cancellationToken))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<bool> TryConnectTcpAsync(string host, int port, CancellationToken cancellationToken)
    {
        try
        {
            using var client = new TcpClient();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            await client.ConnectAsync(host, port, timeout.Token);
            return client.Connected;
        }
        catch
        {
            return false;
        }
    }
}
