using System.Text.Json;
using ExcPanel.TransferAgent.Contracts;
using ExcPanel.TransferAgent.PrivilegedHelper.Handlers;
using ExcPanel.TransferAgent.Tests.Fakes;

namespace ExcPanel.TransferAgent.Tests.Domain;

public class DomainJoinHandlerTests
{
    [Fact]
    public async Task HandleAsync_ConfiguresHostnameBeforeJoin()
    {
        var runner = new FakePrivilegedCommandRunner();
        var joinAttempted = false;
        var testJoinCalls = 0;

        runner.SetHandler("net", args =>
        {
            if (args.Count >= 2 && args[0] == "ads" && args[1] == "testjoin")
            {
                testJoinCalls++;
                return new CommandExecutionResult { ExitCode = testJoinCalls >= 2 ? 0 : 1 };
            }

            if (args.Count >= 2 && args[0] == "ads" && args[1] == "join")
            {
                joinAttempted = true;
                return new CommandExecutionResult { ExitCode = 0 };
            }

            if (args.Count >= 3 && args[0] == "ads" && args[1] == "dns" && args[2] == "register")
            {
                return new CommandExecutionResult { ExitCode = 0 };
            }

            return new CommandExecutionResult { ExitCode = 0 };
        });

        runner.SetHandler("realm", _ => new CommandExecutionResult
        {
            ExitCode = 0,
            Stdout = "dogrumail-demo.com"
        });

        runner.SetHandler("getent", _ => new CommandExecutionResult
        {
            ExitCode = 0,
            Stdout = "group"
        });

        runner.SetHandler("wbinfo", _ => new CommandExecutionResult
        {
            ExitCode = 0,
            Stdout = "ok"
        });

        var handler = new DomainJoinHandler(runner);
        var payload = JsonSerializer.SerializeToElement(new DomainJoinPayload
        {
            DnsDomain = "dogrumail-demo.com",
            Realm = "DOGRUMAIL-DEMO.COM",
            Workgroup = "DOGRUMAIL-DEMO",
            DomainController = "dc.dogrumail-demo.com",
            DomainControllerIp = "10.34.141.2",
            Username = "Administrator",
            Password = "secret",
            ComputerName = "sftp",
            RequiredAdGroup = @"DOGRUMAIL-DEMO\Exchange Trusted Subsystem",
            MainConfigPath = Path.Combine(Path.GetTempPath(), $"smb-{Guid.NewGuid():N}.conf"),
            Krb5ConfPath = Path.Combine(Path.GetTempPath(), $"krb5-{Guid.NewGuid():N}.conf"),
            HostsFilePath = Path.Combine(Path.GetTempPath(), $"hosts-{Guid.NewGuid():N}")
        }, PrivilegedHelperJson.SerializerOptions);

        var response = await handler.HandleAsync("req-1", payload, CancellationToken.None);

        Assert.True(response.Success, response.Message);
        Assert.True(joinAttempted);
        Assert.Contains(
            runner.Invocations,
            invocation => invocation.Key == "hostnamectl"
                && invocation.Arguments.SequenceEqual(new[] { "set-hostname", "sftp.dogrumail-demo.com" }));
        Assert.Equal("sftp.dogrumail-demo.com", response.Data.GetProperty("configuredFqdn").GetString());
    }
}
