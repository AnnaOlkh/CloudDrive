using System.Security.Cryptography;

namespace CloudDrive.Core.Files;

public static class ContentHasher
{
    public static string ToHex(ReadOnlySpan<byte> hash) => Convert.ToHexString(hash).ToLowerInvariant();

    public static async Task<string> ComputeAsync(Stream stream, CancellationToken ct = default)
    {
        var hash = await SHA256.HashDataAsync(stream, ct);
        return ToHex(hash);
    }

    public static string Compute(byte[] data) => ToHex(SHA256.HashData(data));
}
