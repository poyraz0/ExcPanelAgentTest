using ExcPanel.TransferAgent.Contracts;
using ExcPanel.TransferAgent.Endpoints;
using ExcPanel.TransferAgent.Options;
using ExcPanel.TransferAgent.Services.Platform;
using ExcPanel.TransferAgent.Services.Storage;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile(
    "appsettings.Storage.json",
    optional: true,
    reloadOnChange: true);

builder.Services.Configure<TransferAgentOptions>(
    builder.Configuration.GetSection(TransferAgentOptions.SectionName));

builder.Services.AddSingleton<IPlatformInfoService, PlatformInfoService>();
builder.Services.AddSingleton<IStorageService, StorageService>();
builder.Services.AddSingleton<IStorageDiskDiscoveryService, StorageDiskDiscoveryService>();
builder.Services.AddSingleton<ICommandRunner, CommandRunner>();
builder.Services.AddSingleton<IStorageSettingsWriter, StorageSettingsWriter>();
builder.Services.AddSingleton<LinuxStorageConfigurer>();
builder.Services.AddSingleton<IStorageConfigureService, StorageConfigureService>();

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

app.Run();
