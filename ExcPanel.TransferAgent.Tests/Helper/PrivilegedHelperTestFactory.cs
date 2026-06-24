using ExcPanel.TransferAgent.PrivilegedHelper;
using ExcPanel.TransferAgent.PrivilegedHelper.Commands;
using ExcPanel.TransferAgent.PrivilegedHelper.Handlers;
using ExcPanel.TransferAgent.Tests.Fakes;

namespace ExcPanel.TransferAgent.Tests.Helper;

internal static class PrivilegedHelperTestFactory
{
    public static PrivilegedHelperHost CreateHost(
        FakePrivilegedCommandRunner? runner = null,
        bool isRoot = true)
    {
        runner ??= new FakePrivilegedCommandRunner();
        var storageHandler = new StorageConfigureHandler(runner, new DiskInspector(runner));
        var sftpInitializeHandler = new SftpInitializeHandler(runner);
        var sftpUserHandler = new SftpUserHandler(runner);
        var sambaConfigureHandler = new SambaConfigureHandler(runner);
        var testDomainJoinHandler = new TestDomainJoinHandler(runner);
        var domainJoinHandler = new DomainJoinHandler(runner);
        var applyExchangeAclHandler = new ApplyExchangeAclHandler(runner);
        return new PrivilegedHelperHost(storageHandler, sftpInitializeHandler, sftpUserHandler, sambaConfigureHandler, testDomainJoinHandler, domainJoinHandler, applyExchangeAclHandler, () => isRoot);
    }
}
