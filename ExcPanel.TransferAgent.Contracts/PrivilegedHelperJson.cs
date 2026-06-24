using System.Text.Json;

namespace ExcPanel.TransferAgent.Contracts;

public static class PrivilegedHelperJson
{
    public static JsonSerializerOptions SerializerOptions { get; } = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}
