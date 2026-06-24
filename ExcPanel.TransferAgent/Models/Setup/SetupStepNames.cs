namespace ExcPanel.TransferAgent.Models.Setup;

public static class SetupStepNames
{
    public const string SystemPrerequisites = "SystemPrerequisites";
    public const string DiskDiscovery = "DiskDiscovery";
    public const string DiskConfigureDryRun = "DiskConfigureDryRun";
    public const string DiskConfigure = "DiskConfigure";
    public const string DomainPrecheck = "DomainPrecheck";
    public const string DomainJoin = "DomainJoin";
    public const string SambaInitialize = "SambaInitialize";
    public const string SftpInitialize = "SftpInitialize";
    public const string ExportPathTest = "ExportPathTest";
    public const string SambaWriteTest = "SambaWriteTest";
    public const string FinalSummary = "FinalSummary";

    public static IReadOnlyList<string> All { get; } =
    [
        SystemPrerequisites,
        DiskDiscovery,
        DiskConfigureDryRun,
        DiskConfigure,
        DomainPrecheck,
        DomainJoin,
        SambaInitialize,
        SftpInitialize,
        ExportPathTest,
        SambaWriteTest,
        FinalSummary
    ];
}
