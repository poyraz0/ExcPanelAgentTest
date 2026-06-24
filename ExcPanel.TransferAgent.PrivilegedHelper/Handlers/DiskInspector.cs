using System.Text.Json;
using System.Text.Json.Serialization;
using ExcPanel.TransferAgent.PrivilegedHelper.Commands;

namespace ExcPanel.TransferAgent.PrivilegedHelper.Handlers;

public class DiskInspector
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IPrivilegedCommandRunner _commandRunner;

    public DiskInspector(IPrivilegedCommandRunner commandRunner)
    {
        _commandRunner = commandRunner;
    }

    public async Task<DiskInspectionResult?> GetDiskAsync(string diskPath, CancellationToken cancellationToken)
    {
        var result = await _commandRunner.RunAsync(
            "lsblk",
            ["--json", "--bytes", "--output", "NAME,PATH,SIZE,TYPE,MOUNTPOINTS,SERIAL,WWN,PKNAME", diskPath],
            cancellationToken: cancellationToken);

        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.Stdout))
        {
            return null;
        }

        var output = JsonSerializer.Deserialize<LsblkOutput>(result.Stdout, JsonOptions);
        var device = output?.BlockDevices?.FirstOrDefault(d =>
            string.Equals(d.Type, "disk", StringComparison.OrdinalIgnoreCase));

        if (device is null)
        {
            return null;
        }

        var descendants = CollectDescendants(device).ToList();
        var allDevices = new[] { device }.Concat(descendants).ToList();
        var mountPoints = allDevices
            .SelectMany(GetMountPoints)
            .Where(mountPoint => !string.IsNullOrWhiteSpace(mountPoint))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var hasPartitions = descendants.Any(descendant =>
            string.Equals(descendant.Type, "part", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(descendant.Type, "lvm", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(descendant.Type, "crypt", StringComparison.OrdinalIgnoreCase));

        var isSwap = allDevices.Any(descendant =>
            string.Equals(descendant.Type, "swap", StringComparison.OrdinalIgnoreCase));

        var systemMountPoints = new HashSet<string>(StringComparer.Ordinal)
        {
            "/",
            "/boot",
            "/boot/efi"
        };

        return new DiskInspectionResult
        {
            Path = device.Path ?? diskPath,
            SizeBytes = device.Size ?? 0,
            Serial = device.Serial ?? string.Empty,
            Wwn = device.Wwn ?? string.Empty,
            IsMounted = mountPoints.Count > 0,
            MountPoint = mountPoints.FirstOrDefault(),
            HasPartitions = hasPartitions,
            IsSystemDisk = mountPoints.Any(systemMountPoints.Contains) || isSwap
        };
    }

    private static IEnumerable<LsblkDevice> CollectDescendants(LsblkDevice device)
    {
        if (device.Children is null)
        {
            yield break;
        }

        foreach (var child in device.Children)
        {
            yield return child;
            foreach (var descendant in CollectDescendants(child))
            {
                yield return descendant;
            }
        }
    }

    private static IEnumerable<string> GetMountPoints(LsblkDevice device)
    {
        if (device.MountPoints is null)
        {
            yield break;
        }

        foreach (var mountPoint in device.MountPoints)
        {
            if (!string.IsNullOrWhiteSpace(mountPoint))
            {
                yield return mountPoint;
            }
        }
    }

    private sealed class LsblkOutput
    {
        [JsonPropertyName("blockdevices")]
        public List<LsblkDevice>? BlockDevices { get; set; }
    }

    private sealed class LsblkDevice
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("path")]
        public string? Path { get; set; }

        [JsonPropertyName("size")]
        public long? Size { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("mountpoints")]
        public List<string?>? MountPoints { get; set; }

        [JsonPropertyName("serial")]
        public string? Serial { get; set; }

        [JsonPropertyName("wwn")]
        public string? Wwn { get; set; }

        [JsonPropertyName("children")]
        public List<LsblkDevice>? Children { get; set; }
    }
}

public class DiskInspectionResult
{
    public string Path { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string Serial { get; set; } = string.Empty;
    public string Wwn { get; set; } = string.Empty;
    public bool IsMounted { get; set; }
    public string? MountPoint { get; set; }
    public bool HasPartitions { get; set; }
    public bool IsSystemDisk { get; set; }
}
