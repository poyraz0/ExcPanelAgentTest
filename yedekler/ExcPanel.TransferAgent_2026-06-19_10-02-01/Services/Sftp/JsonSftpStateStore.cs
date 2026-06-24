using System.Text.Json;
using ExcPanel.TransferAgent.Contracts;
using ExcPanel.TransferAgent.Models;
using ExcPanel.TransferAgent.Options;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;

namespace ExcPanel.TransferAgent.Services.Sftp;

public class JsonSftpStateStore : ISftpStateStore
{
    private const string StateFileName = "sftp-state.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly string _stateFilePath;

    public JsonSftpStateStore(IWebHostEnvironment environment, IOptions<TransferAgentOptions> options)
    {
        var stateDirectory = string.IsNullOrWhiteSpace(options.Value.StateDirectory)
            ? environment.ContentRootPath
            : options.Value.StateDirectory;
        Directory.CreateDirectory(stateDirectory);
        _stateFilePath = Path.Combine(stateDirectory, StateFileName);
    }

    public async Task<SftpStateDocument> GetAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_stateFilePath))
            {
                return new SftpStateDocument();
            }

            await using var stream = File.OpenRead(_stateFilePath);
            return await JsonSerializer.DeserializeAsync<SftpStateDocument>(stream, JsonOptions, cancellationToken)
                ?? new SftpStateDocument();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAsync(SftpStateDocument state, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
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

    public async Task<SftpUserRecord?> FindByUsernameAsync(string username, CancellationToken cancellationToken = default)
    {
        var state = await GetAsync(cancellationToken);
        return state.Users.FirstOrDefault(user =>
            string.Equals(user.Username, username, StringComparison.Ordinal));
    }

    public async Task<SftpUserRecord?> FindByJobAsync(Guid jobId, JobDirectoryType jobType, CancellationToken cancellationToken = default)
    {
        var state = await GetAsync(cancellationToken);
        return state.Users.FirstOrDefault(user =>
            string.Equals(user.JobId, jobId.ToString("D"), StringComparison.OrdinalIgnoreCase)
            && user.JobType == jobType
            && user.Status != SftpUserStatus.Deleted);
    }

    public async Task UpsertUserAsync(SftpUserRecord record, CancellationToken cancellationToken = default)
    {
        var state = await GetAsync(cancellationToken);
        state.Users.RemoveAll(user => string.Equals(user.Username, record.Username, StringComparison.Ordinal));
        state.Users.Add(record);
        await SaveAsync(state, cancellationToken);
    }

    public async Task RemoveUserAsync(string username, CancellationToken cancellationToken = default)
    {
        var state = await GetAsync(cancellationToken);
        state.Users.RemoveAll(user => string.Equals(user.Username, username, StringComparison.Ordinal));
        await SaveAsync(state, cancellationToken);
    }

    public async Task SetInitializedAsync(bool initialized, CancellationToken cancellationToken = default)
    {
        var state = await GetAsync(cancellationToken);
        state.Initialized = initialized;
        state.InitializedAtUtc = initialized ? DateTime.UtcNow : state.InitializedAtUtc;
        await SaveAsync(state, cancellationToken);
    }
}
