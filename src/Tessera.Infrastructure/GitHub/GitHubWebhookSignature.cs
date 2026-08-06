using System.Security.Cryptography;
using System.Text;

namespace Tessera.Infrastructure.GitHub;

public static class GitHubWebhookSignature
{
    public static bool Verify(string secret, ReadOnlySpan<byte> body, string? signatureHeader)
    {
        const string prefix = "sha256=";
        if (string.IsNullOrEmpty(signatureHeader) || !signatureHeader.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var provided = signatureHeader[prefix.Length..];
        if (provided.Length != 64)
        {
            return false;
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var computed = Convert.ToHexStringLower(hmac.ComputeHash(body.ToArray()));
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(computed),
            Encoding.ASCII.GetBytes(provided));
    }
}
