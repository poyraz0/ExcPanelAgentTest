using System.Text.Json;
using ExcPanel.TransferAgent.Contracts;
using ExcPanel.TransferAgent.Models;
using ExcPanel.TransferAgent.Options;
using ExcPanel.TransferAgent.PrivilegedHelper.Handlers;
using ExcPanel.TransferAgent.Providers.Linux;
using ExcPanel.TransferAgent.Services.Samba;
using ExcPanel.TransferAgent.Services.Sftp;
using ExcPanel.TransferAgent.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using HelperBindMountVerifier = ExcPanel.TransferAgent.PrivilegedHelper.Handlers.LinuxBindMountVerifier;

namespace ExcPanel.TransferAgent.Tests.Sftp;

public class SftpSecurityHelperTests
{
    [Theory]
    [InlineData("7f3a2b1c-0000-0000-0000-000000000001", JobDirectoryType.Export, "exp_7f3a2b1c0000")]
    [InlineData("7f3a2b1c-0000-0000-0000-000000000001", JobDirectoryType.Import, "imp_7f3a2b1c0000")]
    public void GenerateUsername_UsesJobIdPrefix(Guid jobId, JobDirectoryType jobType, string expected)
    {
        var username = SftpUsernameGenerator.Generate(jobId, jobType);
        Assert.Equal(expected, username);
        Assert.Matches("^[a-z0-9_]+$", username);
    }

    [Fact]
    public void GeneratePassword_IsAtLeast24Characters()
    {
        var password = SftpPasswordGenerator.Generate();
        Assert.True(password.Length >= 24);
    }

    [Fact]
    public void ComputeLinuxAccountExpiryDate_UsesDayAfterUtcExpiryDate()
    {
        var expiry = new DateTime(2026, 6, 21, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal("2026-06-22", SftpLinuxAccountExpiry.ComputeExpiryDate(expiry));
    }

    [Fact]
    public async Task StateStore_DoesNotPersistPlaintextPassword()
    {
        var environment = new TestWebHostEnvironment(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        var options = Microsoft.Extensions.Options.Options.Create(new TransferAgentOptions { StateDirectory = environment.ContentRootPath });
        var store = new JsonSftpStateStore(environment, options);

        await store.UpsertUserAsync(new SftpUserRecord
        {
            Username = "exp_7f3a2b1c0000",
            JobId = Guid.NewGuid().ToString("D"),
            JobType = JobDirectoryType.Export,
            ChrootPath = "/var/lib/excpanel-sftp/chroots/exp_7f3a2b1c0000",
            FilesMountPath = "/var/lib/excpanel-sftp/chroots/exp_7f3a2b1c0000/files",
            JobPath = "/data/excpanel-transfer/exports/job",
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddHours(1),
            Status = SftpUserStatus.Active,
            BindMountStatus = SftpBindMountStatus.Active
        });

        var json = await File.ReadAllTextAsync(Path.Combine(environment.ContentRootPath, "sftp-state.json"));
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
    }
}

public class SftpUserHandlerTests : IDisposable
{
    public SftpUserHandlerTests()
    {
        HelperBindMountVerifier.MountInfoReaderOverride = null;
    }

    public void Dispose()
    {
        HelperBindMountVerifier.MountInfoReaderOverride = null;
    }

    [Fact]
    public async Task HandleCreateAsync_RejectsImportJobType()
    {
        var runner = new FakePrivilegedCommandRunner();
        var handler = new SftpUserHandler(runner);
        var payload = CreateValidPayload(SftpJobType.Import);

        var response = await handler.HandleCreateAsync(
            "req-1",
            JsonSerializer.SerializeToElement(payload),
            CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal(PrivilegedHelperErrorCodes.SftpNotImplemented, response.ErrorCode);
    }

    [Fact]
    public async Task HandleCreateAsync_RejectsMissingMarker()
    {
        using var storageRoot = new TempDirectory();
        using var chrootBase = new TempDirectory();
        var jobId = Guid.NewGuid();
        var jobPath = Path.Combine(storageRoot.Path, "exports", jobId.ToString("D"));
        Directory.CreateDirectory(jobPath);

        var runner = CreateSftpRunner();
        var handler = new SftpUserHandler(runner);
        var payload = CreateValidPayload(
            SftpJobType.Export,
            storageRoot.Path,
            jobPath,
            chrootBase.Path,
            jobId);

        var response = await handler.HandleCreateAsync(
            "req-1",
            JsonSerializer.SerializeToElement(payload),
            CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal(PrivilegedHelperErrorCodes.JobMarkerMissing, response.ErrorCode);
    }

    [Fact]
    public async Task HandleCreateAsync_RejectsWhenExportGroupMissing()
    {
        using var storageRoot = new TempDirectory();
        using var chrootBase = new TempDirectory();
        var jobId = Guid.NewGuid();
        var jobPath = CreateJobDirectory(storageRoot.Path, jobId, "Export");

        var runner = CreateSftpRunner();
        var handler = new SftpUserHandler(runner);
        var payload = CreateValidPayload(
            SftpJobType.Export,
            storageRoot.Path,
            jobPath,
            chrootBase.Path,
            jobId);
        payload.ExportGroupName = $"missing-group-{Guid.NewGuid():N}";

        var response = await handler.HandleCreateAsync(
            "req-1",
            JsonSerializer.SerializeToElement(payload),
            CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal(PrivilegedHelperErrorCodes.SftpNotInitialized, response.ErrorCode);
    }

    [Fact]
    public async Task HandleDeleteAsync_PreservesJobDirectory()
    {
        using var storageRoot = new TempDirectory();
        using var chrootBase = new TempDirectory();
        var jobId = Guid.NewGuid();
        var jobPath = CreateJobDirectory(storageRoot.Path, jobId, "Export");
        var username = SftpUsernameGenerator.Generate(jobId, JobDirectoryType.Export);
        var chrootPath = Path.Combine(chrootBase.Path, username);
        Directory.CreateDirectory(chrootPath);
        var filesPath = Path.Combine(chrootPath, "files");
        Directory.CreateDirectory(filesPath);
        File.WriteAllText(Path.Combine(jobPath, "sample.pst"), "data");

        var runner = CreateSftpRunner();
        runner.SetHandler("findmnt", _ => new CommandExecutionResult { ExitCode = 1 });
        var handler = new SftpUserHandler(runner);

        var response = await handler.HandleDeleteAsync(
            "req-1",
            JsonSerializer.SerializeToElement(new SftpUserDeletePayload
            {
                Username = username,
                ChrootPath = chrootPath,
                FilesMountPath = filesPath,
                JobPath = jobPath
            }),
            CancellationToken.None);

        Assert.True(response.Success);
        Assert.True(Directory.Exists(jobPath));
        Assert.True(File.Exists(Path.Combine(jobPath, "sample.pst")));
    }

    [Fact]
    public async Task HandleInitializeAsync_RollsBackWhenSshdValidationFails()
    {
        using var chrootBase = new TempDirectory();
        using var sshdDir = new TempDirectory();
        var dropInPath = Path.Combine(sshdDir.Path, "60-excpanel-sftp.conf");
        File.WriteAllText(dropInPath, "old-config");

        var runner = new FakePrivilegedCommandRunner();
        runner.SetHandler("sshd", _ => new CommandExecutionResult
        {
            ExitCode = 1,
            Stderr = "invalid config"
        });

        var handler = new SftpInitializeHandler(runner);
        var response = await handler.HandleInitializeAsync(
            "req-1",
            JsonSerializer.SerializeToElement(new SftpInitializePayload
            {
                ChrootBasePath = chrootBase.Path,
                ExportGroupName = "excpanel-sftp-export",
                ImportGroupName = "excpanel-sftp-import",
                SshdDropInPath = dropInPath
            }),
            CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal(PrivilegedHelperErrorCodes.SshdValidationFailed, response.ErrorCode);
        Assert.Equal("old-config", await File.ReadAllTextAsync(dropInPath));
    }

    [Fact]
    public async Task HandleDeleteAsync_SucceedsWhenLinuxUserAlreadyAbsent()
    {
        using var storageRoot = new TempDirectory();
        using var chrootBase = new TempDirectory();
        var jobId = Guid.NewGuid();
        var jobPath = CreateJobDirectory(storageRoot.Path, jobId, "Export");
        var username = SftpUsernameGenerator.Generate(jobId, JobDirectoryType.Export);
        var chrootPath = Path.Combine(chrootBase.Path, username);
        Directory.CreateDirectory(chrootPath);
        var filesPath = Path.Combine(chrootPath, "files");
        Directory.CreateDirectory(filesPath);

        var runner = CreateSftpRunner();
        var handler = new SftpUserHandler(runner);
        var response = await handler.HandleDeleteAsync(
            "req-1",
            JsonSerializer.SerializeToElement(new SftpUserDeletePayload
            {
                Username = username,
                ChrootPath = chrootPath,
                FilesMountPath = filesPath,
                JobPath = jobPath
            }),
            CancellationToken.None);

        Assert.True(response.Success);
        var result = response.Data.Deserialize<SftpUserDeleteResultData>(PrivilegedHelperJson.SerializerOptions);
        Assert.NotNull(result);
        Assert.True(result!.UserDeleted);
        Assert.True(result.JobPathPreserved);
        Assert.Contains("LinuxUserAlreadyAbsent", result.AuditSteps);
    }

    [Fact]
    public async Task HandleDeleteAsync_AttemptsLazyUnmountWhenNeeded()
    {
        using var storageRoot = new TempDirectory();
        using var chrootBase = new TempDirectory();
        var jobId = Guid.NewGuid();
        var jobPath = CreateJobDirectory(storageRoot.Path, jobId, "Export");
        var username = SftpUsernameGenerator.Generate(jobId, JobDirectoryType.Export);
        var chrootPath = Path.Combine(chrootBase.Path, username);
        Directory.CreateDirectory(chrootPath);
        var filesPath = Path.Combine(chrootPath, "files");
        Directory.CreateDirectory(filesPath);

        var runner = new FakePrivilegedCommandRunner();
        var umountAttempts = 0;
        runner.SetHandler("findmnt", _ => new CommandExecutionResult { ExitCode = 0, Stdout = jobPath });
        runner.SetHandler("umount", _ =>
        {
            umountAttempts++;
            return new CommandExecutionResult
            {
                ExitCode = umountAttempts == 1 ? 1 : 0
            };
        });

        var handler = new SftpUserHandler(runner);
        var response = await handler.HandleDeleteAsync(
            "req-1",
            JsonSerializer.SerializeToElement(new SftpUserDeletePayload
            {
                Username = username,
                ChrootPath = chrootPath,
                FilesMountPath = filesPath,
                JobPath = jobPath
            }),
            CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(2, umountAttempts);
    }

    [Fact]
    public async Task HandleCreateAsync_IdempotentUser_StillSetsPassword()
    {
        if (!File.ReadLines("/etc/group").Any(line => line.StartsWith("excpanel-sftp-export:", StringComparison.Ordinal)))
        {
            return;
        }

        using var storageRoot = new TempDirectory();
        using var chrootBase = new TempDirectory();
        var jobId = Guid.NewGuid();
        var jobPath = CreateJobDirectory(storageRoot.Path, jobId, "Export");
        var payload = CreateValidPayload(
            SftpJobType.Export,
            storageRoot.Path,
            jobPath,
            chrootBase.Path,
            jobId);

        var runner = CreateSftpRunner();
        var handler = new SftpUserHandler(runner);

        var first = await handler.HandleCreateAsync(
            "req-1",
            JsonSerializer.SerializeToElement(payload),
            CancellationToken.None);
        Assert.True(first.Success);

        payload.PlaintextPassword = SftpPasswordGenerator.Generate();
        var second = await handler.HandleCreateAsync(
            "req-2",
            JsonSerializer.SerializeToElement(payload),
            CancellationToken.None);
        Assert.True(second.Success);

        Assert.Equal(2, runner.Invocations.Count(invocation => invocation.Key == "chpasswd"));
    }

    private static FakePrivilegedCommandRunner CreateSftpRunner()
    {
        var runner = new FakePrivilegedCommandRunner();
        var mountedPaths = new HashSet<string>(StringComparer.Ordinal);
        var bindTargets = new Dictionary<string, string>(StringComparer.Ordinal);
        var bindSources = new Dictionary<string, string>(StringComparer.Ordinal);

        runner.SetHandler("findmnt", arguments =>
        {
            var args = arguments.ToList();
            var outputIndex = args.FindIndex(argument => argument == "-o");
            var outputColumns = outputIndex >= 0 && outputIndex + 1 < args.Count
                ? args[outputIndex + 1]
                : string.Empty;

            var mountpointIndex = args.FindIndex(argument => argument is "-M" or "--mountpoint");
            if (mountpointIndex >= 0 && mountpointIndex + 1 < args.Count)
            {
                var mountpoint = args[mountpointIndex + 1];
                if (!mountedPaths.Contains(mountpoint))
                {
                    return new CommandExecutionResult { ExitCode = 1 };
                }

                if (outputColumns.Contains("FSROOT", StringComparison.OrdinalIgnoreCase))
                {
                    var jobPath = bindTargets[mountpoint];
                    var sourceRoot = bindSources[mountpoint];
                    var fsRoot = "/" + Path.GetRelativePath(sourceRoot, jobPath).Replace('\\', '/');
                    return new CommandExecutionResult
                    {
                        ExitCode = 0,
                        Stdout = $"{fsRoot} {sourceRoot}"
                    };
                }

                return new CommandExecutionResult { ExitCode = 0, Stdout = mountpoint };
            }

            var sourceIndex = args.FindIndex(argument => argument is "-S" or "--source");
            if (sourceIndex >= 0 && sourceIndex + 1 < args.Count)
            {
                var source = args[sourceIndex + 1];
                var match = bindSources.FirstOrDefault(pair => string.Equals(pair.Value, source, StringComparison.Ordinal));
                if (!string.IsNullOrEmpty(match.Key))
                {
                    return new CommandExecutionResult { ExitCode = 0, Stdout = source };
                }
            }

            return new CommandExecutionResult { ExitCode = 1 };
        });

        runner.SetHandler("mount", arguments =>
        {
            if (arguments.Count >= 2 && arguments[0] == "--make-rshared")
            {
                return new CommandExecutionResult { ExitCode = 0 };
            }

            if (arguments.Count >= 3
                && arguments[0] == "--bind"
                && Directory.Exists(arguments[1]))
            {
                var source = arguments[1];
                var target = arguments[2];
                var storageRoot = Directory.GetParent(source)?.Parent?.FullName ?? source;

                if (Directory.Exists(target))
                {
                    Directory.Delete(target, recursive: false);
                }

                Directory.CreateSymbolicLink(target, source);
                bindTargets[target] = source;
                bindSources[target] = storageRoot;
                mountedPaths.Add(target);
                ConfigureBindMountMountInfo(storageRoot, source, target);
                return new CommandExecutionResult { ExitCode = 0 };
            }

            return new CommandExecutionResult { ExitCode = 1, Stderr = "bind mount failed" };
        });

        runner.SetHandler("umount", arguments =>
        {
            var target = arguments[^1];
            if (Directory.Exists(target))
            {
                Directory.Delete(target, recursive: false);
            }

            mountedPaths.Remove(target);
            bindTargets.Remove(target);
            bindSources.Remove(target);
            HelperBindMountVerifier.MountInfoReaderOverride = null;
            return new CommandExecutionResult { ExitCode = 0 };
        });

        return runner;
    }

    private static void ConfigureBindMountMountInfo(string storageRoot, string jobPath, string filesMountPath)
    {
        var relative = Path.GetRelativePath(storageRoot, jobPath).Replace('\\', '/').TrimStart('/');
        var fsRoot = "/" + relative;
        const string device = "/dev/fake-excpanel-test";

        HelperBindMountVerifier.MountInfoReaderOverride = () =>
        [
            $"100 99 8:0 {fsRoot} {filesMountPath} rw,relatime shared:1 - ext4 {device} rw",
            $"99 1 8:0 / {storageRoot} rw,relatime shared:1 - ext4 {device} rw"
        ];
    }

    private static SftpUserCreatePayload CreateValidPayload(
        SftpJobType jobType,
        string? storageRoot = null,
        string? jobPath = null,
        string? chrootBase = null,
        Guid? jobId = null)
    {
        jobId ??= Guid.NewGuid();
        storageRoot ??= "/data/excpanel-transfer";
        jobPath ??= Path.Combine(storageRoot, "exports", jobId.Value.ToString("D"));
        chrootBase ??= "/var/lib/excpanel-sftp/chroots";

        return new SftpUserCreatePayload
        {
            Username = SftpUsernameGenerator.Generate(jobId.Value, jobType == SftpJobType.Export ? JobDirectoryType.Export : JobDirectoryType.Import),
            PlaintextPassword = SftpPasswordGenerator.Generate(),
            JobType = jobType,
            JobId = jobId.Value.ToString("D"),
            StorageRootPath = storageRoot,
            JobPath = jobPath,
            ChrootBasePath = chrootBase,
            ExportGroupName = "excpanel-sftp-export",
            ImportGroupName = "excpanel-sftp-import",
            ExpiresAtUtc = DateTime.UtcNow.AddHours(2),
            MarkerFileName = LinuxJobDirectoryProvider.MarkerFileName
        };
    }

    private static string CreateJobDirectory(string storageRoot, Guid jobId, string jobType)
    {
        var jobPath = Path.Combine(storageRoot, "exports", jobId.ToString("D"));
        Directory.CreateDirectory(jobPath);
        var marker = JsonSerializer.Serialize(new JobDirectoryMarker
        {
            JobId = jobId.ToString("D"),
            JobType = JobDirectoryType.Export,
            CreatedAtUtc = DateTime.UtcNow,
            AgentVersion = "test"
        }, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        File.WriteAllText(Path.Combine(jobPath, LinuxJobDirectoryProvider.MarkerFileName), marker);
        return jobPath;
    }

    [Fact]
    public void CreatePayload_UsesExportGroupForExportJobs()
    {
        var payload = CreateValidPayload(SftpJobType.Export);
        Assert.Equal("excpanel-sftp-export", payload.ExportGroupName);
        Assert.Equal(SftpJobType.Export, payload.JobType);
    }

    [Fact]
    public async Task HandleStatusAsync_EnsureBindMount_AppliesMountAndPermissions()
    {
        using var storageRoot = new TempDirectory();
        using var chrootBase = new TempDirectory();
        var jobId = Guid.NewGuid();
        var jobPath = CreateJobDirectory(storageRoot.Path, jobId, "Export");
        File.WriteAllText(Path.Combine(jobPath, "sample.pst"), "data");
        var username = SftpUsernameGenerator.Generate(jobId, JobDirectoryType.Export);
        var chrootPath = Path.Combine(chrootBase.Path, username);
        var filesPath = Path.Combine(chrootPath, "files");

        var runner = CreateSftpRunner();
        var handler = new SftpUserHandler(runner);
        var response = await handler.HandleStatusAsync(
            "req-1",
            JsonSerializer.SerializeToElement(new SftpUserStatusPayload
            {
                Username = username,
                ChrootPath = chrootPath,
                FilesMountPath = filesPath,
                JobPath = jobPath,
                ExportGroupName = "excpanel-sftp-export",
                EnsureBindMount = true
            }),
            CancellationToken.None);

        Assert.True(response.Success);
        Assert.Contains(runner.Invocations, invocation =>
            invocation.Key == "mount"
            && invocation.Arguments.Contains("--bind"));
        Assert.Contains(runner.Invocations, invocation =>
            invocation.Key == "chown"
            && invocation.Arguments.Contains("-R")
            && invocation.Arguments.Contains(":excpanel-sftp-export"));
        Assert.Contains(runner.Invocations, invocation =>
            invocation.Key == "chmod"
            && invocation.Arguments.Contains("-R")
            && invocation.Arguments.Contains("u=rwX,g=rX,o="));
        Assert.Contains(runner.Invocations, invocation =>
            invocation.Key == "chmod"
            && invocation.Arguments.Contains("2750"));
    }

    [Fact]
    public async Task HandleStatusAsync_EnsureBindMount_ReplacesWrongMountSource()
    {
        using var storageRoot = new TempDirectory();
        using var chrootBase = new TempDirectory();
        using var oldStorageRoot = new TempDirectory();
        var jobId = Guid.NewGuid();
        var jobPath = CreateJobDirectory(storageRoot.Path, jobId, "Export");
        var wrongJobPath = CreateJobDirectory(oldStorageRoot.Path, Guid.NewGuid(), "Export");
        var username = SftpUsernameGenerator.Generate(jobId, JobDirectoryType.Export);
        var chrootPath = Path.Combine(chrootBase.Path, username);
        var filesPath = Path.Combine(chrootPath, "files");

        var runner = CreateSftpRunner();
        Directory.CreateDirectory(chrootPath);
        Directory.CreateSymbolicLink(filesPath, wrongJobPath);

        var handler = new SftpUserHandler(runner);
        var response = await handler.HandleStatusAsync(
            "req-1",
            JsonSerializer.SerializeToElement(new SftpUserStatusPayload
            {
                Username = username,
                ChrootPath = chrootPath,
                FilesMountPath = filesPath,
                JobPath = jobPath,
                ExportGroupName = "excpanel-sftp-export",
                EnsureBindMount = true
            }),
            CancellationToken.None);

        Assert.True(response.Success);
        Assert.Contains(runner.Invocations, invocation =>
            invocation.Key == "mount"
            && invocation.Arguments.SequenceEqual(["--bind", jobPath, filesPath]));
    }

    [Fact]
    public async Task HandleStatusAsync_EnsureBindMount_FailsWhenJobDirectoryMissing()
    {
        using var chrootBase = new TempDirectory();
        var jobId = Guid.NewGuid();
        var username = SftpUsernameGenerator.Generate(jobId, JobDirectoryType.Export);
        var chrootPath = Path.Combine(chrootBase.Path, username);
        var filesPath = Path.Combine(chrootPath, "files");

        var runner = CreateSftpRunner();
        var handler = new SftpUserHandler(runner);
        var response = await handler.HandleStatusAsync(
            "req-1",
            JsonSerializer.SerializeToElement(new SftpUserStatusPayload
            {
                Username = username,
                ChrootPath = chrootPath,
                FilesMountPath = filesPath,
                JobPath = Path.Combine(chrootBase.Path, "missing-job"),
                ExportGroupName = "excpanel-sftp-export",
                EnsureBindMount = true
            }),
            CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal(PrivilegedHelperErrorCodes.InvalidJobPath, response.ErrorCode);
    }

    [Fact]
    public void BindMountVerifier_DetectsActiveBindMount_FromMountInfo()
    {
        using var storageRoot = new TempDirectory();
        using var chrootBase = new TempDirectory();
        var jobId = Guid.NewGuid();
        var jobPath = CreateJobDirectory(storageRoot.Path, jobId, "Export");
        var filesPath = Path.Combine(chrootBase.Path, "files");
        Directory.CreateDirectory(filesPath);

        ConfigureBindMountMountInfo(storageRoot.Path, jobPath, filesPath);

        Assert.True(HelperBindMountVerifier.IsBindMountActive(filesPath, jobPath));
        Assert.True(HelperBindMountVerifier.IsMountPoint(filesPath));
    }

    [Fact]
    public async Task HandleCreateAsync_CreatesChrootRootWithRootOwnership()
    {
        if (!File.ReadLines("/etc/group").Any(line => line.StartsWith("excpanel-sftp-export:", StringComparison.Ordinal)))
        {
            return;
        }

        using var storageRoot = new TempDirectory();
        using var chrootBase = new TempDirectory();
        var jobId = Guid.NewGuid();
        var jobPath = CreateJobDirectory(storageRoot.Path, jobId, "Export");

        var runner = CreateSftpRunner();
        var handler = new SftpUserHandler(runner);
        var payload = CreateValidPayload(
            SftpJobType.Export,
            storageRoot.Path,
            jobPath,
            chrootBase.Path,
            jobId);

        var response = await handler.HandleCreateAsync(
            "req-1",
            JsonSerializer.SerializeToElement(payload),
            CancellationToken.None);

        Assert.True(response.Success);
        Assert.Contains(runner.Invocations, invocation =>
            invocation.Key == "chown"
            && invocation.Arguments.Contains("root:root"));
        Assert.Contains(runner.Invocations, invocation =>
            invocation.Key == "chmod"
            && invocation.Arguments.Contains("0755"));
    }
}

public class SftpServiceTests
{
    [Fact]
    public async Task CreateUserAsync_RejectsInvalidJobId()
    {
        var service = CreateService(new FakePrivilegedHelperClient());
        var result = await service.CreateUserAsync(new CreateSftpUserRequest
        {
            JobId = "not-a-guid",
            JobType = "Export",
            ExpiresAtUtc = DateTime.UtcNow.AddHours(1)
        });

        Assert.Equal(SftpOperationStatus.BadRequest, result.Status);
    }

    [Fact]
    public async Task CreateUserAsync_RejectsImportJobType()
    {
        var service = CreateService(new FakePrivilegedHelperClient());
        var result = await service.CreateUserAsync(new CreateSftpUserRequest
        {
            JobId = Guid.NewGuid().ToString("D"),
            JobType = "Import",
            ExpiresAtUtc = DateTime.UtcNow.AddHours(1)
        });

        Assert.Equal(SftpOperationStatus.NotImplemented, result.Status);
    }

    [Fact]
    public async Task CreateUserAsync_ReturnsConflictWhenJobAlreadyBound()
    {
        var jobId = Guid.NewGuid();
        using var environment = new TempDirectory();
        var hostEnvironment = new TestWebHostEnvironment(environment.Path);
        var options = Microsoft.Extensions.Options.Options.Create(new TransferAgentOptions
        {
            EnableSftp = true,
            RequireStorageMount = false,
            StorageRootPath = environment.Path,
            StateDirectory = environment.Path
        });
        var stateStore = new JsonSftpStateStore(hostEnvironment, options);
        await stateStore.SetInitializedAsync(true);
        await stateStore.UpsertUserAsync(new SftpUserRecord
        {
            Username = "exp_otheruser000",
            JobId = jobId.ToString("D"),
            JobType = JobDirectoryType.Export,
            ChrootPath = "/var/lib/excpanel-sftp/chroots/exp_otheruser000",
            FilesMountPath = "/var/lib/excpanel-sftp/chroots/exp_otheruser000/files",
            JobPath = Path.Combine(environment.Path, "exports", jobId.ToString("D")),
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddHours(1),
            Status = SftpUserStatus.Active,
            BindMountStatus = SftpBindMountStatus.Active
        });

        var service = CreateService(new FakePrivilegedHelperClient(), stateStore, options);
        var result = await service.CreateUserAsync(new CreateSftpUserRequest
        {
            JobId = jobId.ToString("D"),
            JobType = "Export",
            ExpiresAtUtc = DateTime.UtcNow.AddHours(2)
        });

        Assert.Equal(SftpOperationStatus.Conflict, result.Status);
    }

    [Fact]
    public async Task ReconcileExpiredUsersAsync_DisablesThenDeletesAfterGracePeriod()
    {
        using var environment = new TempDirectory();
        var hostEnvironment = new TestWebHostEnvironment(environment.Path);
        var options = Microsoft.Extensions.Options.Options.Create(new TransferAgentOptions
        {
            EnableSftp = true,
            StateDirectory = hostEnvironment.ContentRootPath,
            SftpDeletionGracePeriodMinutes = 0
        });
        var stateStore = new JsonSftpStateStore(hostEnvironment, options);
        await stateStore.SetInitializedAsync(true);
        await stateStore.UpsertUserAsync(new SftpUserRecord
        {
            Username = "exp_deadbeef0000",
            JobId = Guid.NewGuid().ToString("D"),
            JobType = JobDirectoryType.Export,
            ChrootPath = "/var/lib/excpanel-sftp/chroots/exp_deadbeef0000",
            FilesMountPath = "/var/lib/excpanel-sftp/chroots/exp_deadbeef0000/files",
            JobPath = "/data/excpanel-transfer/exports/job",
            CreatedAtUtc = DateTime.UtcNow.AddHours(-2),
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-30),
            Status = SftpUserStatus.Active,
            BindMountStatus = SftpBindMountStatus.Active
        });

        var helperClient = new FakePrivilegedHelperClient();
        var service = CreateService(helperClient, stateStore, options);
        await service.ReconcileExpiredUsersAsync();

        Assert.Contains(helperClient.Invocations, action => action == PrivilegedHelperActions.SftpUserDisable);
        Assert.Contains(helperClient.Invocations, action => action == PrivilegedHelperActions.SftpUserDelete);
        Assert.Null(await stateStore.FindByUsernameAsync("exp_deadbeef0000"));
    }

    private static SftpService CreateService(
        IPrivilegedHelperClient helperClient,
        ISftpStateStore? stateStore = null,
        IOptions<TransferAgentOptions>? options = null)
    {
        var tempDirectory = new TempDirectory();
        var environment = new TestWebHostEnvironment(tempDirectory.Path);
        options ??= Microsoft.Extensions.Options.Options.Create(new TransferAgentOptions
        {
            EnableSftp = true,
            RequireStorageMount = false,
            StorageRootPath = environment.ContentRootPath,
            StateDirectory = environment.ContentRootPath
        });
        stateStore ??= new JsonSftpStateStore(environment, options);

        return new SftpService(
            options,
            helperClient,
            stateStore,
            new DefaultSftpAuthorizationService(),
            new FakeStorageMountChecker(),
            new LinuxJobDirectoryProvider(
                options,
                Microsoft.Extensions.Options.Options.Create(new SambaOptions { Enabled = false, StorageRoot = environment.ContentRootPath }),
                new FakeStorageMountChecker(),
                new SambaPathService(
                    Microsoft.Extensions.Options.Options.Create(new SambaOptions { Enabled = false, StorageRoot = environment.ContentRootPath }),
                    options),
                new FakeExchangeAclService(),
                NullLogger<LinuxJobDirectoryProvider>.Instance),
            NullLogger<SftpService>.Instance);
    }
}

internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup.
        }
    }
}

internal sealed class TestWebHostEnvironment : Microsoft.AspNetCore.Hosting.IWebHostEnvironment
{
    public TestWebHostEnvironment(string contentRootPath)
    {
        ContentRootPath = contentRootPath;
        WebRootPath = contentRootPath;
    }

    public string EnvironmentName { get; set; } = "Development";
    public string ApplicationName { get; set; } = "Tests";
    public string ContentRootPath { get; set; }
    public string WebRootPath { get; set; }
    public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = null!;
}

internal sealed class FakePrivilegedHelperClient : IPrivilegedHelperClient
{
    public List<string> Invocations { get; } = [];

    public Task<PrivilegedHelperResponse> InvokeAsync(PrivilegedHelperRequest request, CancellationToken cancellationToken = default)
    {
        Invocations.Add(request.Action);
        var data = request.Action switch
        {
            PrivilegedHelperActions.SftpUserDisable => JsonSerializer.SerializeToElement(new SftpUserDisableResultData
            {
                Username = "exp_deadbeef0000",
                PasswordLocked = true
            }),
            PrivilegedHelperActions.SftpUserDelete => JsonSerializer.SerializeToElement(new SftpUserDeleteResultData
            {
                Username = "exp_deadbeef0000",
                UserDeleted = true,
                BindMountRemoved = true,
                ChrootRemoved = true,
                JobPathPreserved = true,
                AuditSteps = ["DisabledUser", "DeletedLinuxUser"]
            }),
            _ => JsonDocument.Parse("{}").RootElement.Clone()
        };

        return Task.FromResult(PrivilegedHelperResponse.Ok(
            request.RequestId,
            "ok",
            Array.Empty<string>(),
            data));
    }
}
