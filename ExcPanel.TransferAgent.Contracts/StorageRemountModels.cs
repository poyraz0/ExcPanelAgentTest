namespace ExcPanel.TransferAgent.Contracts;

public class StorageRemountPayload
{
    public string MountPath { get; set; } = string.Empty;
}

public class StorageRemountResultData
{
    public string MountPath { get; set; } = string.Empty;
    public bool Mounted { get; set; }
}
