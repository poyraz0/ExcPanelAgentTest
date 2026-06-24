using System.Text.Json;
using ExcPanel.TransferAgent.Contracts;
using ExcPanel.TransferAgent.Models.Setup;
using ExcPanel.TransferAgent.Options;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ExcPanel.TransferAgent.Services.Setup;

public class JsonSetupStateStore : ISetupStateStore
{
    private const string StateFileName = "setup-state.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly string _stateFilePath;

    public JsonSetupStateStore(IWebHostEnvironment environment, IOptions<TransferAgentOptions> options, IHostEnvironment hostEnvironment)
    {
        var stateDirectory = ResolveStateDirectory(environment, options.Value, hostEnvironment);
        Directory.CreateDirectory(stateDirectory);
        _stateFilePath = Path.Combine(stateDirectory, StateFileName);
    }

    public async Task<SetupState> GetAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_stateFilePath))
            {
                return CreateDefaultState();
            }

            await using var stream = File.OpenRead(_stateFilePath);
            return await JsonSerializer.DeserializeAsync<SetupState>(stream, JsonOptions, cancellationToken)
                ?? CreateDefaultState();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAsync(SetupState state, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            state.UpdatedAtUtc = DateTime.UtcNow;
            var tempPath = $"{_stateFilePath}.{Guid.NewGuid():N}.tmp";
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, state, JsonOptions, cancellationToken);
            }

            File.Move(tempPath, _stateFilePath, overwrite: true);
        }
        finally
        {
            _lock.Release();
        }
    }

    private static SetupState CreateDefaultState() => new()
    {
        SetupId = Guid.NewGuid().ToString("D"),
        Status = SetupStatus.NotStarted,
        CurrentStep = string.Empty,
        Steps = SetupStepNames.All.Select(name => new SetupStepState
        {
            StepName = name,
            Status = SetupStepStatus.Pending
        }).ToList()
    };

    internal static string ResolveStateDirectory(
        IWebHostEnvironment environment,
        TransferAgentOptions options,
        IHostEnvironment hostEnvironment)
    {
        if (!string.IsNullOrWhiteSpace(options.StateDirectory))
        {
            return options.StateDirectory;
        }

        if (hostEnvironment.IsDevelopment())
        {
            return environment.ContentRootPath;
        }

        return "/var/lib/excpanel-transfer-agent";
    }
}
