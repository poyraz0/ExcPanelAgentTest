using ExcPanel.TransferAgent.Contracts;
using ExcPanel.TransferAgent.Endpoints;
using ExcPanel.TransferAgent.Options;
using ExcPanel.TransferAgent.Providers.Linux;
using ExcPanel.TransferAgent.Services.Platform;
using ExcPanel.TransferAgent.Services.PrivilegedHelper;
using ExcPanel.TransferAgent.Services.Sftp;
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

builder.Services.AddSingleton<IPlatformInfoService, PlatformInfoService>();
builder.Services.AddSingleton<IStorageService, StorageService>();
builder.Services.AddSingleton<IStorageDiskDiscoveryService, StorageDiskDiscoveryService>();
builder.Services.AddSingleton<IStorageSettingsWriter, StorageSettingsWriter>();
builder.Services.AddSingleton<IConfirmationTokenService, ConfirmationTokenService>();
builder.Services.AddSingleton<IPrivilegedHelperClient, LinuxPrivilegedHelperClient>();
builder.Services.AddSingleton<IStorageConfigureService, StorageConfigureService>();
builder.Services.AddSingleton<IStorageMountChecker, LinuxStorageMountChecker>();
builder.Services.AddSingleton<IJobDirectoryProvider, LinuxJobDirectoryProvider>();
builder.Services.AddSingleton<ISftpAuthorizationService, DefaultSftpAuthorizationService>();
builder.Services.AddSingleton<ISftpStateStore, JsonSftpStateStore>();
builder.Services.AddSingleton<ISftpService, SftpService>();
builder.Services.AddHostedService<SftpCleanupHostedService>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.MapAgentEndpoints();
app.MapStorageEndpoints();
app.MapSftpEndpoints();

app.Run();
