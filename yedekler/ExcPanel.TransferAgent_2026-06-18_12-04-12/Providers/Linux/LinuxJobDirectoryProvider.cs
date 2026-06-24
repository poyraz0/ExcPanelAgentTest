using System.Reflection;
using System.Text.Json;
using ExcPanel.TransferAgent.Common;
using ExcPanel.TransferAgent.Contracts;
using ExcPanel.TransferAgent.Models;
using ExcPanel.TransferAgent.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExcPanel.TransferAgent.Providers.Linux;

public class LinuxJobDirectoryProvider : IJobDirectoryProvider
{
    public const string MarkerFileName = ".excpanel-job.json";

    private static readonly HashSet<string> ForbiddenStorageRoots = new(StringComparer.Ordinal)
    {
        "/",
        "/data"
    };

    private static readonly JsonSerializerOptions MarkerJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly TransferAgentOptions _options;
    private readonly IStorageMountChecker _mountChecker;
    private readonly ILogger<LinuxJobDirectoryProvider> _logger;

    public LinuxJobDirectoryProvider(
        IOptions<TransferAgentOptions> options,
        IStorageMountChecker mountChecker,
        ILogger<LinuxJobDirectoryProvider> logger)
    {
        _options = options.Value;
        _mountChecker = mountChecker;
        _logger = logger;
    }

    public async Task<JobDirectoryOperationResult<JobDirectoryCreateResponse>> CreateAsync(
        Guid jobId,
        JobDirectoryType jobType,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var storageValidation = ValidateStorageRoot();
        if (storageValidation is not null)
        {
            return storageValidation;
        }

        if (!await _mountChecker.IsMountedAsync(_options.StorageRootPath, cancellationToken))
        {
            _logger.LogWarning("Storage root is not mounted at {StorageRootPath}", _options.StorageRootPath);
            return JobDirectoryOperationResult<JobDirectoryCreateResponse>.Fail(
                JobDirectoryOperationStatus.ServiceUnavailable,
                "Storage is not mounted.");
        }

        if (!TryResolveJobDirectory(jobId, jobType, out var relativePath, out var physicalPath, out var pathError))
        {
            return JobDirectoryOperationResult<JobDirectoryCreateResponse>.Fail(
                JobDirectoryOperationStatus.BadRequest,
                pathError!);
        }

        if (IsProtectedDirectory(physicalPath))
        {
            return JobDirectoryOperationResult<JobDirectoryCreateResponse>.Fail(
                JobDirectoryOperationStatus.Conflict,
                "The requested path is protected and cannot be modified.");
        }

        var symlinkError = ValidateNoSymlinksInPath(physicalPath);
        if (symlinkError is not null)
        {
            return JobDirectoryOperationResult<JobDirectoryCreateResponse>.Fail(
                JobDirectoryOperationStatus.Conflict,
                symlinkError);
        }

        var directoryExists = Directory.Exists(physicalPath);
        if (directoryExists)
        {
            var markerResult = await ReadMarkerAsync(physicalPath, cancellationToken);
            if (markerResult.Error is not null)
            {
                return JobDirectoryOperationResult<JobDirectoryCreateResponse>.Fail(
                    markerResult.Status,
                    markerResult.Error);
            }

            if (markerResult.Marker is null)
            {
                return JobDirectoryOperationResult<JobDirectoryCreateResponse>.Fail(
                    JobDirectoryOperationStatus.Conflict,
                    "Job directory exists but was not created by the transfer agent.");
            }

            if (!MarkerMatches(markerResult.Marker, jobId, jobType))
            {
                return JobDirectoryOperationResult<JobDirectoryCreateResponse>.Fail(
                    JobDirectoryOperationStatus.Conflict,
                    "Job directory marker does not match the requested job.");
            }

            _logger.LogInformation(
                "Job directory already exists for {JobType} job {JobId} at {PhysicalPath}",
                jobType,
                jobId,
                physicalPath);

            return JobDirectoryOperationResult<JobDirectoryCreateResponse>.Ok(
                BuildCreateResponse(jobId, jobType, relativePath, physicalPath, created: false));
        }

        try
        {
            Directory.CreateDirectory(physicalPath);
            await ApplyDirectoryPermissionsAsync(physicalPath, cancellationToken);

            var marker = new JobDirectoryMarker
            {
                JobId = jobId.ToString("D"),
                JobType = jobType,
                CreatedAtUtc = DateTime.UtcNow,
                AgentVersion = GetAgentVersion()
            };

            await WriteMarkerAtomicallyAsync(physicalPath, marker, cancellationToken);

            _logger.LogInformation(
                "Created job directory for {JobType} job {JobId} at {PhysicalPath}",
                jobType,
                jobId,
                physicalPath);

            return JobDirectoryOperationResult<JobDirectoryCreateResponse>.Ok(
                BuildCreateResponse(jobId, jobType, relativePath, physicalPath, created: true),
                "Job directory prepared successfully.");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Permission denied creating job directory at {PhysicalPath}", physicalPath);
            return JobDirectoryOperationResult<JobDirectoryCreateResponse>.Fail(
                JobDirectoryOperationStatus.Forbidden,
                "Permission denied while preparing the job directory.");
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Failed to create job directory at {PhysicalPath}", physicalPath);
            return JobDirectoryOperationResult<JobDirectoryCreateResponse>.Fail(
                JobDirectoryOperationStatus.Conflict,
                "Job directory could not be created.");
        }
    }

    public async Task<JobDirectoryOperationResult<JobDirectoryInfoResponse>> GetAsync(
        Guid jobId,
        JobDirectoryType jobType,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var storageValidation = ValidateStorageRoot();
        if (storageValidation is not null)
        {
            return MapStorageValidationToInfo(storageValidation);
        }

        if (!await _mountChecker.IsMountedAsync(_options.StorageRootPath, cancellationToken))
        {
            return JobDirectoryOperationResult<JobDirectoryInfoResponse>.Fail(
                JobDirectoryOperationStatus.ServiceUnavailable,
                "Storage is not mounted.");
        }

        if (!TryResolveJobDirectory(jobId, jobType, out var relativePath, out var physicalPath, out var pathError))
        {
            return JobDirectoryOperationResult<JobDirectoryInfoResponse>.Fail(
                JobDirectoryOperationStatus.BadRequest,
                pathError!);
        }

        if (!Directory.Exists(physicalPath))
        {
            return JobDirectoryOperationResult<JobDirectoryInfoResponse>.Fail(
                JobDirectoryOperationStatus.NotFound,
                "Job directory was not found.");
        }

        var markerResult = await ReadMarkerAsync(physicalPath, cancellationToken);
        DateTime? createdAtUtc = markerResult.Marker?.CreatedAtUtc;

        if (createdAtUtc is null)
        {
            try
            {
                createdAtUtc = Directory.GetCreationTimeUtc(physicalPath);
            }
            catch
            {
                createdAtUtc = null;
            }
        }

        var (fileCount, totalBytes) = CountFiles(physicalPath);

        return JobDirectoryOperationResult<JobDirectoryInfoResponse>.Ok(new JobDirectoryInfoResponse
        {
            JobId = jobId.ToString("D"),
            JobType = jobType,
            RelativePath = relativePath,
            PhysicalPath = physicalPath,
            Exists = true,
            CreatedAtUtc = createdAtUtc,
            TotalFileCount = fileCount,
            TotalFileSizeBytes = totalBytes
        });
    }

    public async Task<JobDirectoryOperationResult<JobDirectoryDeleteResponse>> DeleteAsync(
        Guid jobId,
        JobDirectoryType jobType,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var storageValidation = ValidateStorageRoot();
        if (storageValidation is not null)
        {
            return MapStorageValidationToDelete(storageValidation);
        }

        if (!await _mountChecker.IsMountedAsync(_options.StorageRootPath, cancellationToken))
        {
            return JobDirectoryOperationResult<JobDirectoryDeleteResponse>.Fail(
                JobDirectoryOperationStatus.ServiceUnavailable,
                "Storage is not mounted.");
        }

        if (!TryResolveJobDirectory(jobId, jobType, out var relativePath, out var physicalPath, out var pathError))
        {
            return JobDirectoryOperationResult<JobDirectoryDeleteResponse>.Fail(
                JobDirectoryOperationStatus.BadRequest,
                pathError!);
        }

        if (IsProtectedDirectory(physicalPath))
        {
            return JobDirectoryOperationResult<JobDirectoryDeleteResponse>.Fail(
                JobDirectoryOperationStatus.Conflict,
                "The requested path is protected and cannot be deleted.");
        }

        if (!Directory.Exists(physicalPath))
        {
            return JobDirectoryOperationResult<JobDirectoryDeleteResponse>.Fail(
                JobDirectoryOperationStatus.NotFound,
                "Job directory was not found.");
        }

        var symlinkError = ValidateNoSymlinksInPath(physicalPath);
        if (symlinkError is not null)
        {
            return JobDirectoryOperationResult<JobDirectoryDeleteResponse>.Fail(
                JobDirectoryOperationStatus.Conflict,
                symlinkError);
        }

        var markerResult = await ReadMarkerAsync(physicalPath, cancellationToken);
        if (markerResult.Error is not null)
        {
            return JobDirectoryOperationResult<JobDirectoryDeleteResponse>.Fail(
                markerResult.Status,
                markerResult.Error);
        }

        if (markerResult.Marker is null)
        {
            return JobDirectoryOperationResult<JobDirectoryDeleteResponse>.Fail(
                JobDirectoryOperationStatus.Conflict,
                "Job directory cannot be deleted because it was not created by the transfer agent.");
        }

        if (!MarkerMatches(markerResult.Marker, jobId, jobType))
        {
            return JobDirectoryOperationResult<JobDirectoryDeleteResponse>.Fail(
                JobDirectoryOperationStatus.Conflict,
                "Job directory marker does not match the requested job.");
        }

        if (!IsDirectoryEmptyExceptMarker(physicalPath))
        {
            return JobDirectoryOperationResult<JobDirectoryDeleteResponse>.Fail(
                JobDirectoryOperationStatus.Conflict,
                "Job directory is not empty and cannot be deleted.");
        }

        try
        {
            var markerPath = Path.Combine(physicalPath, MarkerFileName);
            if (File.Exists(markerPath))
            {
                File.Delete(markerPath);
            }

            Directory.Delete(physicalPath);

            _logger.LogInformation(
                "Deleted job directory for {JobType} job {JobId} at {PhysicalPath}",
                jobType,
                jobId,
                physicalPath);

            return JobDirectoryOperationResult<JobDirectoryDeleteResponse>.Ok(new JobDirectoryDeleteResponse
            {
                JobId = jobId.ToString("D"),
                JobType = jobType,
                RelativePath = relativePath,
                Deleted = true
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Permission denied deleting job directory at {PhysicalPath}", physicalPath);
            return JobDirectoryOperationResult<JobDirectoryDeleteResponse>.Fail(
                JobDirectoryOperationStatus.Forbidden,
                "Permission denied while deleting the job directory.");
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Failed to delete job directory at {PhysicalPath}", physicalPath);
            return JobDirectoryOperationResult<JobDirectoryDeleteResponse>.Fail(
                JobDirectoryOperationStatus.Conflict,
                "Job directory could not be deleted.");
        }
    }

    private JobDirectoryOperationResult<JobDirectoryCreateResponse>? ValidateStorageRoot()
    {
        var rootPath = _options.StorageRootPath?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return JobDirectoryOperationResult<JobDirectoryCreateResponse>.Fail(
                JobDirectoryOperationStatus.BadRequest,
                "Storage root path is not configured.");
        }

        if (ForbiddenStorageRoots.Contains(rootPath))
        {
            return JobDirectoryOperationResult<JobDirectoryCreateResponse>.Fail(
                JobDirectoryOperationStatus.BadRequest,
                "Storage root path is not allowed.");
        }

        if (rootPath.Contains("..", StringComparison.Ordinal))
        {
            return JobDirectoryOperationResult<JobDirectoryCreateResponse>.Fail(
                JobDirectoryOperationStatus.BadRequest,
                "Storage root path contains invalid traversal sequences.");
        }

        string normalizedRoot;
        try
        {
            normalizedRoot = Path.GetFullPath(rootPath);
        }
        catch
        {
            return JobDirectoryOperationResult<JobDirectoryCreateResponse>.Fail(
                JobDirectoryOperationStatus.BadRequest,
                "Storage root path could not be resolved.");
        }

        if (ForbiddenStorageRoots.Contains(normalizedRoot))
        {
            return JobDirectoryOperationResult<JobDirectoryCreateResponse>.Fail(
                JobDirectoryOperationStatus.BadRequest,
                "Storage root path is not allowed.");
        }

        if (!Directory.Exists(normalizedRoot))
        {
            return JobDirectoryOperationResult<JobDirectoryCreateResponse>.Fail(
                JobDirectoryOperationStatus.ServiceUnavailable,
                "Storage root path does not exist.");
        }

        if (IsSymlinkOrReparsePoint(normalizedRoot))
        {
            return JobDirectoryOperationResult<JobDirectoryCreateResponse>.Fail(
                JobDirectoryOperationStatus.Conflict,
                "Storage root path must not be a symlink.");
        }

        return null;
    }

    internal bool TryResolveJobDirectory(
        Guid jobId,
        JobDirectoryType jobType,
        out string relativePath,
        out string physicalPath,
        out string? error)
    {
        relativePath = string.Empty;
        physicalPath = string.Empty;
        error = null;

        var rootPath = _options.StorageRootPath?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            error = "Storage root path is not configured.";
            return false;
        }

        var segment = GetJobTypeSegment(jobType);
        relativePath = $"{segment}/{jobId:D}";

        try
        {
            var combined = Path.Combine(rootPath, segment, jobId.ToString("D"));
            physicalPath = Path.GetFullPath(combined);
            var normalizedRoot = Path.GetFullPath(rootPath);

            if (!IsSubPath(normalizedRoot, physicalPath))
            {
                error = "Resolved job directory path is outside the storage root.";
                return false;
            }
        }
        catch
        {
            error = "Job directory path could not be resolved.";
            return false;
        }

        return true;
    }

    internal bool IsProtectedDirectory(string physicalPath)
    {
        var normalizedRoot = Path.GetFullPath(_options.StorageRootPath.Trim());
        var normalizedTarget = Path.GetFullPath(physicalPath);
        var exportsRoot = Path.GetFullPath(Path.Combine(normalizedRoot, "exports"));
        var importsRoot = Path.GetFullPath(Path.Combine(normalizedRoot, "imports"));

        return string.Equals(normalizedTarget, normalizedRoot, StringComparison.Ordinal)
            || string.Equals(normalizedTarget, exportsRoot, StringComparison.Ordinal)
            || string.Equals(normalizedTarget, importsRoot, StringComparison.Ordinal)
            || ForbiddenStorageRoots.Contains(normalizedTarget);
    }

    internal static string GetJobTypeSegment(JobDirectoryType jobType) =>
        jobType switch
        {
            JobDirectoryType.Export => "exports",
            JobDirectoryType.Import => "imports",
            _ => throw new ArgumentOutOfRangeException(nameof(jobType), jobType, "Unsupported job type.")
        };

    internal static bool IsSubPath(string rootPath, string candidatePath)
    {
        var normalizedRoot = Path.GetFullPath(rootPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var normalizedCandidate = Path.GetFullPath(candidatePath);

        return normalizedCandidate.StartsWith(normalizedRoot, StringComparison.Ordinal);
    }

    internal static bool MarkerMatches(JobDirectoryMarker marker, Guid jobId, JobDirectoryType jobType) =>
        string.Equals(marker.JobId, jobId.ToString("D"), StringComparison.OrdinalIgnoreCase)
        && marker.JobType == jobType;

    internal static bool IsDirectoryEmptyExceptMarker(string directoryPath)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(directoryPath))
        {
            var name = Path.GetFileName(entry);
            if (!string.Equals(name, MarkerFileName, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    internal static (int FileCount, long TotalBytes) CountFiles(string directoryPath)
    {
        var count = 0;
        long totalBytes = 0;

        foreach (var file in Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories))
        {
            count++;
            try
            {
                totalBytes += new FileInfo(file).Length;
            }
            catch
            {
                // Ignore unreadable files for aggregate stats.
            }
        }

        return (count, totalBytes);
    }

    private async Task<(JobDirectoryMarker? Marker, JobDirectoryOperationStatus Status, string? Error)> ReadMarkerAsync(
        string directoryPath,
        CancellationToken cancellationToken)
    {
        var markerPath = Path.Combine(directoryPath, MarkerFileName);
        if (!File.Exists(markerPath))
        {
            return (null, JobDirectoryOperationStatus.Success, null);
        }

        try
        {
            await using var stream = File.OpenRead(markerPath);
            var marker = await JsonSerializer.DeserializeAsync<JobDirectoryMarker>(stream, MarkerJsonOptions, cancellationToken);
            return (marker, JobDirectoryOperationStatus.Success, null);
        }
        catch (JsonException)
        {
            return (null, JobDirectoryOperationStatus.Conflict, "Job directory marker is invalid.");
        }
        catch (UnauthorizedAccessException)
        {
            return (null, JobDirectoryOperationStatus.Forbidden, "Permission denied while reading the job directory marker.");
        }
        catch (IOException)
        {
            return (null, JobDirectoryOperationStatus.Conflict, "Job directory marker could not be read.");
        }
    }

    private static async Task WriteMarkerAtomicallyAsync(
        string directoryPath,
        JobDirectoryMarker marker,
        CancellationToken cancellationToken)
    {
        var markerPath = Path.Combine(directoryPath, MarkerFileName);
        var tempPath = Path.Combine(directoryPath, $".{MarkerFileName}.{Guid.NewGuid():N}.tmp");

        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, marker, MarkerJsonOptions, cancellationToken);
        }

        File.Move(tempPath, markerPath, overwrite: true);
    }

    private async Task ApplyDirectoryPermissionsAsync(string directoryPath, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var mode = _options.StorageDirectoryMode.TrimStart('0');
        if (string.IsNullOrWhiteSpace(mode))
        {
            mode = _options.StorageDirectoryMode;
        }

        var (exitCode, _, error) = await ProcessRunner.RunAsync(
            "chmod",
            $"{mode} \"{directoryPath}\"",
            cancellationToken);

        if (exitCode != 0)
        {
            throw new UnauthorizedAccessException(
                string.IsNullOrWhiteSpace(error)
                    ? "Failed to apply directory permissions."
                    : error.Trim());
        }
    }

    private static string? ValidateNoSymlinksInPath(string physicalPath)
    {
        var current = physicalPath;
        while (!string.IsNullOrEmpty(current))
        {
            if (Directory.Exists(current) && IsSymlinkOrReparsePoint(current))
            {
                return "Job directory path resolves through a symlink.";
            }

            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent) || string.Equals(parent, current, StringComparison.Ordinal))
            {
                break;
            }

            current = parent;
        }

        return null;
    }

    private static bool IsSymlinkOrReparsePoint(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Attributes.HasFlag(FileAttributes.ReparsePoint);
        }
        catch
        {
            return false;
        }
    }

    private static string GetAgentVersion() =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";

    private static JobDirectoryCreateResponse BuildCreateResponse(
        Guid jobId,
        JobDirectoryType jobType,
        string relativePath,
        string physicalPath,
        bool created) => new()
    {
        JobId = jobId.ToString("D"),
        JobType = jobType,
        RelativePath = relativePath,
        PhysicalPath = physicalPath,
        Created = created
    };

    private static JobDirectoryOperationResult<JobDirectoryInfoResponse> MapStorageValidationToInfo(
        JobDirectoryOperationResult<JobDirectoryCreateResponse> validation) =>
        JobDirectoryOperationResult<JobDirectoryInfoResponse>.Fail(validation.Status, validation.Message!);

    private static JobDirectoryOperationResult<JobDirectoryDeleteResponse> MapStorageValidationToDelete(
        JobDirectoryOperationResult<JobDirectoryCreateResponse> validation) =>
        JobDirectoryOperationResult<JobDirectoryDeleteResponse>.Fail(validation.Status, validation.Message!);
}
