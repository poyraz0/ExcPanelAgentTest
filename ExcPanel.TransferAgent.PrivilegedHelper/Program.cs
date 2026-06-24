using ExcPanel.TransferAgent.PrivilegedHelper.Commands;
using ExcPanel.TransferAgent.PrivilegedHelper.Handlers;

namespace ExcPanel.TransferAgent.PrivilegedHelper;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var commandRunner = new LinuxPrivilegedCommandRunner();
        var diskInspector = new DiskInspector(commandRunner);
        var storageHandler = new StorageConfigureHandler(commandRunner, diskInspector);
        var storageRemountHandler = new StorageRemountHandler(commandRunner);
        var sftpInitializeHandler = new SftpInitializeHandler(commandRunner);
        var sftpUserHandler = new SftpUserHandler(commandRunner);
        var sambaConfigureHandler = new SambaConfigureHandler(commandRunner);
        var testDomainJoinHandler = new TestDomainJoinHandler(commandRunner);
        var domainJoinHandler = new DomainJoinHandler(commandRunner);
        var applyExchangeAclHandler = new ApplyExchangeAclHandler(commandRunner);
        var host = new PrivilegedHelperHost(storageHandler, sftpInitializeHandler, sftpUserHandler, sambaConfigureHandler, testDomainJoinHandler, domainJoinHandler, applyExchangeAclHandler, storageRemountHandler);
        return await host.RunAsync(args, Console.In, Console.Out);
    }
}
