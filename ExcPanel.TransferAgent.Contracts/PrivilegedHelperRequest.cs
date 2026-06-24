using System.Text.Json;

namespace ExcPanel.TransferAgent.Contracts;

public class PrivilegedHelperRequest
{
    public string RequestId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public JsonElement Payload { get; set; }
}
