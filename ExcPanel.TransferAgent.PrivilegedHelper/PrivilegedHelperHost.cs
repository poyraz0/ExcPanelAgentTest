using System.Runtime.InteropServices;
using System.Text.Json;
using ExcPanel.TransferAgent.Contracts;
using ExcPanel.TransferAgent.PrivilegedHelper.Handlers;

namespace ExcPanel.TransferAgent.PrivilegedHelper;

public class PrivilegedHelperHost
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly StorageConfigureHandler _storageConfigureHandler;
    private readonly SftpInitializeHandler _sftpInitializeHandler;
    private readonly SftpUserHandler _sftpUserHandler;
    private readonly SambaConfigureHandler _sambaConfigureHandler;
    private readonly TestDomainJoinHandler _testDomainJoinHandler;
    private readonly DomainJoinHandler _domainJoinHandler;
    private readonly ApplyExchangeAclHandler _applyExchangeAclHandler;
    private readonly StorageRemountHandler _storageRemountHandler;
    private readonly Func<bool> _isRootCheck;

    public PrivilegedHelperHost(
        StorageConfigureHandler storageConfigureHandler,
        SftpInitializeHandler sftpInitializeHandler,
        SftpUserHandler sftpUserHandler,
        SambaConfigureHandler sambaConfigureHandler,
        TestDomainJoinHandler testDomainJoinHandler,
        DomainJoinHandler domainJoinHandler,
        ApplyExchangeAclHandler applyExchangeAclHandler,
        StorageRemountHandler storageRemountHandler,
        Func<bool>? isRootCheck = null)
    {
        _storageConfigureHandler = storageConfigureHandler;
        _sftpInitializeHandler = sftpInitializeHandler;
        _sftpUserHandler = sftpUserHandler;
        _sambaConfigureHandler = sambaConfigureHandler;
        _testDomainJoinHandler = testDomainJoinHandler;
        _domainJoinHandler = domainJoinHandler;
        _applyExchangeAclHandler = applyExchangeAclHandler;
        _storageRemountHandler = storageRemountHandler;
        _isRootCheck = isRootCheck ?? IsRunningAsRoot;
    }

    public async Task<int> RunAsync(string[] args, TextReader input, TextWriter output, CancellationToken cancellationToken = default)
    {
        if (args.Length > 0)
        {
            var rejection = PrivilegedHelperResponse.Failure(
                string.Empty,
                PrivilegedHelperErrorCodes.InvalidArguments,
                "Command-line arguments are not accepted. Provide JSON on stdin.");
            await WriteResponseAsync(output, rejection);
            return 1;
        }

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var rejection = PrivilegedHelperResponse.Failure(
                string.Empty,
                PrivilegedHelperErrorCodes.NotLinux,
                "Privileged helper is only supported on Linux.");
            await WriteResponseAsync(output, rejection);
            return 1;
        }

        if (!_isRootCheck())
        {
            var rejection = PrivilegedHelperResponse.Failure(
                string.Empty,
                PrivilegedHelperErrorCodes.NotRoot,
                "Privileged helper must run as root.");
            await WriteResponseAsync(output, rejection);
            return 1;
        }

        PrivilegedHelperRequest? request;
        try
        {
            var json = await input.ReadToEndAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(json))
            {
                var rejection = PrivilegedHelperResponse.Failure(
                    string.Empty,
                    PrivilegedHelperErrorCodes.InvalidArguments,
                    "Request body is required on stdin.");
                await WriteResponseAsync(output, rejection);
                return 1;
            }

            request = JsonSerializer.Deserialize<PrivilegedHelperRequest>(json, JsonOptions);
            if (request is null)
            {
                var rejection = PrivilegedHelperResponse.Failure(
                    string.Empty,
                    PrivilegedHelperErrorCodes.InvalidArguments,
                    "Request body could not be parsed.");
                await WriteResponseAsync(output, rejection);
                return 1;
            }
        }
        catch (JsonException)
        {
            var rejection = PrivilegedHelperResponse.Failure(
                string.Empty,
                PrivilegedHelperErrorCodes.InvalidArguments,
                "Request body must be valid JSON.");
            await WriteResponseAsync(output, rejection);
            return 1;
        }

        var requestId = string.IsNullOrWhiteSpace(request.RequestId)
            ? Guid.NewGuid().ToString("D")
            : request.RequestId.Trim();

        PrivilegedHelperResponse response;
        try
        {
            response = request.Action switch
            {
                PrivilegedHelperActions.StorageConfigure => await _storageConfigureHandler.HandleAsync(
                    requestId,
                    request.Payload,
                    cancellationToken),
                PrivilegedHelperActions.StorageRemount => await _storageRemountHandler.HandleAsync(
                    requestId,
                    request.Payload,
                    cancellationToken),
                PrivilegedHelperActions.SftpInitialize => await _sftpInitializeHandler.HandleInitializeAsync(
                    requestId,
                    request.Payload,
                    cancellationToken),
                PrivilegedHelperActions.SftpUserCreate => await _sftpUserHandler.HandleCreateAsync(
                    requestId,
                    request.Payload,
                    cancellationToken),
                PrivilegedHelperActions.SftpUserDisable => await _sftpUserHandler.HandleDisableAsync(
                    requestId,
                    request.Payload,
                    cancellationToken),
                PrivilegedHelperActions.SftpUserDelete => await _sftpUserHandler.HandleDeleteAsync(
                    requestId,
                    request.Payload,
                    cancellationToken),
                PrivilegedHelperActions.SftpUserStatus => await _sftpUserHandler.HandleStatusAsync(
                    requestId,
                    request.Payload,
                    cancellationToken),
                PrivilegedHelperActions.SftpStatus => await _sftpInitializeHandler.HandleStatusAsync(
                    requestId,
                    request.Payload,
                    cancellationToken),
                PrivilegedHelperActions.SambaConfigure => await _sambaConfigureHandler.HandleAsync(
                    requestId,
                    request.Payload,
                    cancellationToken),
                PrivilegedHelperActions.TestDomainJoin => await _testDomainJoinHandler.HandleAsync(
                    requestId,
                    cancellationToken),
                PrivilegedHelperActions.DomainJoin => await _domainJoinHandler.HandleAsync(
                    requestId,
                    request.Payload,
                    cancellationToken),
                PrivilegedHelperActions.PermissionsApplyExchangeAcl => await _applyExchangeAclHandler.HandleAsync(
                    requestId,
                    request.Payload,
                    cancellationToken),
                _ => PrivilegedHelperResponse.Failure(
                    requestId,
                    PrivilegedHelperErrorCodes.UnknownAction,
                    $"Action '{request.Action}' is not supported.")
            };
        }
        catch (Exception ex)
        {
            response = PrivilegedHelperResponse.Failure(
                requestId,
                PrivilegedHelperErrorCodes.InternalError,
                "An unexpected error occurred while processing the request.",
                stderr: ex.Message);
        }

        await WriteResponseAsync(output, response);
        return response.Success ? 0 : 1;
    }

    private static async Task WriteResponseAsync(TextWriter output, PrivilegedHelperResponse response)
    {
        var json = JsonSerializer.Serialize(response, JsonOptions);
        await output.WriteLineAsync(json);
    }

    private static bool IsRunningAsRoot()
    {
        try
        {
            return geteuid() == 0;
        }
        catch
        {
            return false;
        }
    }

    [DllImport("libc", SetLastError = true)]
    private static extern uint geteuid();
}
