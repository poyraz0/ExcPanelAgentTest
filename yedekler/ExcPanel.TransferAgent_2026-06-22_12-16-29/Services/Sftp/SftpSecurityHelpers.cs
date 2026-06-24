using System.Security.Cryptography;
using System.Text;
using ExcPanel.TransferAgent.Models;

namespace ExcPanel.TransferAgent.Services.Sftp;

public static class SftpUsernameGenerator
{
    public static string Generate(Guid jobId, JobDirectoryType jobType)
    {
        var prefix = jobType switch
        {
            JobDirectoryType.Export => "exp",
            JobDirectoryType.Import => "imp",
            _ => throw new ArgumentOutOfRangeException(nameof(jobType), jobType, "Unsupported job type.")
        };

        var hex = jobId.ToString("N")[..12].ToLowerInvariant();
        return $"{prefix}_{hex}";
    }
}

public static class SftpPasswordGenerator
{
    private const string Alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_@-";

    public static string Generate(int length = 32)
    {
        if (length < 24)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "Password length must be at least 24 characters.");
        }

        var bytes = RandomNumberGenerator.GetBytes(length);
        var builder = new StringBuilder(length);
        foreach (var value in bytes)
        {
            builder.Append(Alphabet[value % Alphabet.Length]);
        }

        return builder.ToString();
    }
}
