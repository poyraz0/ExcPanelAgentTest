using ExcPanel.TransferAgent.Contracts;
using ExcPanel.TransferAgent.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExcPanel.TransferAgent.Services.Sftp;

public class SftpCleanupHostedService : BackgroundService
{
    private readonly ISftpService _sftpService;
    private readonly IOptions<TransferAgentOptions> _options;
    private readonly ILogger<SftpCleanupHostedService> _logger;

    public SftpCleanupHostedService(
        ISftpService sftpService,
        IOptions<TransferAgentOptions> options,
        ILogger<SftpCleanupHostedService> logger)
    {
        _sftpService = sftpService;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await ReconcileSafeAsync(stoppingToken);

        var interval = TimeSpan.FromSeconds(Math.Max(30, _options.Value.SftpCleanupIntervalSeconds));
        using var timer = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await ReconcileSafeAsync(stoppingToken);
        }
    }

    private async Task ReconcileSafeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _sftpService.ReconcileBindMountsAsync(cancellationToken);
            await _sftpService.ReconcileExpiredUsersAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "SFTP cleanup reconciliation failed.");
        }
    }
}
