using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using ExcPanel.TransferAgent.Common;
using ExcPanel.TransferAgent.Contracts;
using ExcPanel.TransferAgent.Models;

namespace ExcPanel.TransferAgent.Services.Storage;

public class StorageDiskDiscoveryService : IStorageDiskDiscoveryService
{
    private static readonly HashSet<string> SystemMountPoints = new(StringComparer.Ordinal)
    {
        "/",
        "/boot",
        "/boot/efi"
    };

    public async Task<StorageDiskDiscoveryResponse> DiscoverDisksAsync(CancellationToken cancellationToken = default)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return new StorageDiskDiscoveryResponse
            {
                Supported = false,
                Platform = GetPlatformName(),
                Message = "Disk discovery is not implemented for this platform."
            };
        }

        try
        {
            var (exitCode, output, error) = await ProcessRunner.RunAsync(
                "lsblk",
                "--json --bytes --output NAME,PATH,SIZE,TYPE,MOUNTPOINTS,PKNAME",
                cancellationToken);

            if (exitCode != 0)
            {
                return new StorageDiskDiscoveryResponse
                {
                    Supported = true,
                    Platform = "Linux",
                    Message = $"Failed to run lsblk: {error.Trim()}"
                };
            }

            var lsblkOutput = JsonSerializer.Deserialize<LsblkOutput>(output, JsonOptions);
            if (lsblkOutput?.BlockDevices is null)
            {
                return new StorageDiskDiscoveryResponse
                {
                    Supported = true,
                    Platform = "Linux",
                    Message = "Failed to parse lsblk output."
                };
            }

            var disks = lsblkOutput.BlockDevices
                .Where(device => string.Equals(device.Type, "disk", StringComparison.OrdinalIgnoreCase))
                .Select(MapDisk)
                .OrderBy(disk => disk.Name, StringComparer.Ordinal)
                .ToList();

            return new StorageDiskDiscoveryResponse
            {
                Supported = true,
                Platform = "Linux",
                Disks = disks
            };
        }
        catch (Exception ex)
        {
            return new StorageDiskDiscoveryResponse
            {
                Supported = true,
                Platform = "Linux",
                Message = $"Disk discovery failed: {ex.Message}"
            };
        }
    }

    private StorageDiskInfo MapDisk(LsblkDevice device)
    {
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

        var isSystemDisk = mountPoints.Any(SystemMountPoints.Contains) || isSwap;
        var isMounted = mountPoints.Count > 0;
        var recommended = !isSystemDisk && !isMounted && !hasPartitions;

        var reason = recommended
            ? "Raw unmounted disk suitable for storage configuration."
            : BuildNotRecommendedReason(isSystemDisk, isMounted, hasPartitions, isSwap);

        return new StorageDiskInfo
        {
            Name = device.Name ?? string.Empty,
            Path = device.Path ?? $"/dev/{device.Name}",
            SizeBytes = device.Size ?? 0,
            SizeGb = ByteSizeHelper.ToGigabytes(device.Size ?? 0),
            Type = device.Type ?? "disk",
            IsMounted = isMounted,
            MountPoint = mountPoints.FirstOrDefault(),
            HasPartitions = hasPartitions,
            IsSystemDisk = isSystemDisk,
            Recommended = recommended,
            Reason = reason
        };
    }

    private static string BuildNotRecommendedReason(bool isSystemDisk, bool isMounted, bool hasPartitions, bool isSwap)
    {
        var reasons = new List<string>();

        if (isSystemDisk)
        {
            reasons.Add("System disk");
        }

        if (isSwap)
        {
            reasons.Add("Contains swap");
        }

        if (isMounted)
        {
            reasons.Add("Disk is mounted");
        }

        if (hasPartitions)
        {
            reasons.Add("Disk has partitions or LVM volumes");
        }

        return string.Join("; ", reasons);
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

    private static string GetPlatformName()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return "Windows";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return "Linux";
        }

        return "Unknown";
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

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

        [JsonPropertyName("pkname")]
        public string? ParentName { get; set; }

        [JsonPropertyName("children")]
        public List<LsblkDevice>? Children { get; set; }
    }
}
