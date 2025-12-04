using System.Security.Cryptography;
using System.Text;

namespace Drasicrhsit.Infrastructure;

public static class DedupeKeyHasher
{
    // Computes dedupe key as: childId + ":" + SHA256(wishlistJson)
    public static string Compute(string childId, string wishlistJson)
    {
        var bytes = Encoding.UTF8.GetBytes(wishlistJson);
        var hash = SHA256.HashData(bytes);
        var hex = Convert.ToHexString(hash).ToLowerInvariant();
        return $"{childId}:{hex}";
    }
}
