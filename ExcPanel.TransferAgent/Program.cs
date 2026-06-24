using System.Runtime.InteropServices;
using ExcPanel.TransferAgent.Common;
using ExcPanel.TransferAgent.Contracts;
using ExcPanel.TransferAgent.Endpoints;
using ExcPanel.TransferAgent.Models;
using ExcPanel.TransferAgent.Options;
using ExcPanel.TransferAgent.Providers.Linux;
using ExcPanel.TransferAgent.Providers.Windows;
using ExcPanel.TransferAgent.Services.Platform;
using ExcPanel.TransferAgent.Services.PrivilegedHelper;
using ExcPanel.TransferAgent.Services.Samba;
using ExcPanel.TransferAgent.Services.Sftp;
using ExcPanel.TransferAgent.Services.Domain;
using ExcPanel.TransferAgent.Services.Export;
using ExcPanel.TransferAgent.Services.Import;
using ExcPanel.TransferAgent.Services.Setup;
using ExcPanel.TransferAgent.Services.Storage;

var builder = WebApplication.CreateBuilder(args);

var stateDirectory = builder.Configuration["TransferAgent:StateDirectory"];
if (string.IsNullOrWhiteSpace(stateDirectory))
{
    stateDirectory = builder.Environment.ContentRootPath;
}

builder.Configuration.AddJsonFile(
    Path.Combine(stateDirectory, "appsettings.Storage.json"),
    optional: true,
    reloadOnChange: true);

builder.Configuration.AddJsonFile(
    Path.Combine(stateDirectory, "appsettings.Setup.json"),
    optional: true,
    reloadOnChange: true);

builder.Services.AddOptions<TransferAgentOptions>()
    .Bind(builder.Configuration.GetSection(TransferAgentOptions.SectionName))
    .PostConfigure<IConfiguration>((options, configuration) =>
    {
        var storageRootPath = configuration["Storage:RootPath"];
        if (!string.IsNullOrWhiteSpace(storageRootPath))
        {
            options.StorageRootPath = storageRootPath;
        }
    });

builder.Services.AddOptions<SambaOptions>()
    .Bind(builder.Configuration.GetSection(SambaOptions.SectionName))
    .PostConfigure<IConfiguration>((options, configuration) =>
    {
        var storageRootPath = configuration["Storage:RootPath"];
        if (!string.IsNullOrWhiteSpace(storageRootPath))
        {
            options.StorageRoot = storageRootPath;
        }

        var transferAgentSection = configuration.GetSection(TransferAgentOptions.SectionName);
        var legacyShareName = transferAgentSection["SambaShareName"];
        if (!string.IsNullOrWhiteSpace(legacyShareName) &&
            string.Equals(options.ShareName, "PSTTransfer$", StringComparison.Ordinal))
        {
            options.ShareName = legacyShareName;
        }

        var legacyEnableSamba = transferAgentSection.GetValue<bool?>("EnableSamba");
        if (legacyEnableSamba.HasValue && !configuration.GetSection(SambaOptions.SectionName).GetValue<bool?>("Enabled").HasValue)
        {
            options.Enabled = legacyEnableSamba.Value;
        }
    });

builder.Services.AddOptions<SetupOptions>()
    .Bind(builder.Configuration.GetSection(SetupOptions.SectionName));

builder.Services.AddOptions<AgentSecurityOptions>()
    .Bind(builder.Configuration.GetSection(AgentSecurityOptions.SectionName));

builder.Services.AddSingleton<ISafeProcessRunner, SafeProcessRunner>();
builder.Services.AddSingleton<SambaPathService>();
builder.Services.AddSingleton<SambaProbeService>();
builder.Services.AddSingleton<ISambaProvider>(sp =>
{
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
    {
        return sp.GetRequiredService<LinuxSambaProvider>();
    }

    return sp.GetRequiredService<WindowsSmbProvider>();
});
builder.Services.AddSingleton<LinuxSambaProvider>();
builder.Services.AddSingleton<WindowsSmbProvider>();

builder.Services.AddSingleton<IPlatformInfoService, PlatformInfoService>();
builder.Services.AddSingleton<IStorageService, StorageService>();
builder.Services.AddSingleton<IStorageDiskDiscoveryService, StorageDiskDiscoveryService>();
builder.Services.AddSingleton<IStorageSettingsWriter, StorageSettingsWriter>();
builder.Services.AddSingleton<IConfirmationTokenService, ConfirmationTokenService>();
builder.Services.AddSingleton<IPrivilegedHelperClient, LinuxPrivilegedHelperClient>();
builder.Services.AddSingleton<IPrivilegedCommandExecutor, LinuxPrivilegedCommandExecutor>();
builder.Services.AddSingleton<IStorageConfigureService, StorageConfigureService>();
builder.Services.AddSingleton<IStorageMountChecker, LinuxStorageMountChecker>();
builder.Services.AddSingleton<IStorageMountRecoveryService, StorageMountRecoveryService>();
builder.Services.AddSingleton<IJobDirectoryProvider, LinuxJobDirectoryProvider>();
builder.Services.AddSingleton<ISftpAuthorizationService, DefaultSftpAuthorizationService>();
builder.Services.AddSingleton<ISftpStateStore, JsonSftpStateStore>();
builder.Services.AddSingleton<ISftpService, SftpService>();
builder.Services.AddHostedService<SftpCleanupHostedService>();

builder.Services.AddSingleton<ISetupStateStore, JsonSetupStateStore>();
builder.Services.AddSingleton<ISetupConfigStore, JsonSetupConfigStore>();
builder.Services.AddSingleton<ISetupPrerequisitesService, SetupPrerequisitesService>();
builder.Services.AddSingleton<ISetupOrchestrationService, SetupOrchestrationService>();
builder.Services.AddSingleton<ISetupConfigService, SetupConfigService>();
builder.Services.AddSingleton<IDomainService, DomainService>();
builder.Services.AddSingleton<IExchangeAclService, ExchangeAclService>();
builder.Services.AddSingleton<IExportPrepareService, ExportPrepareService>();
builder.Services.AddSingleton<IImportPrepareService, ImportPrepareService>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var feature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
        var message = feature?.Error.Message ?? "An unexpected error occurred.";
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(ApiResponse<ErrorResponse>.Fail(message));
    });
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.MapAgentEndpoints();
app.MapStorageEndpoints();
app.MapSftpEndpoints();
app.MapSambaEndpoints();
app.MapSetupEndpoints();
app.MapDomainEndpoints();
app.MapExportEndpoints();
app.MapImportEndpoints();

app.Run();
