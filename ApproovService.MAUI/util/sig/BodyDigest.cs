// ApproovService.MAUI/util/sig/BodyDigest.cs
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace Approov;

internal static class BodyDigest
{
    private static readonly System.Collections.Generic.HashSet<HttpMethod> _digestMethods =
        new() { HttpMethod.Post, HttpMethod.Put, HttpMethod.Patch };

    internal static async Task<bool> TryAddAsync(HttpRequestMessage request)
    {
        if (!_digestMethods.Contains(request.Method)) return false;
        if (request.Content == null) return false;
        try
        {
            await request.Content.LoadIntoBufferAsync();
            byte[] body = await request.Content.ReadAsByteArrayAsync();
            byte[] hash = SHA256.HashData(body);
            string encoded = System.Convert.ToBase64String(hash);
            request.Content.Headers.TryAddWithoutValidation(
                "Content-Digest", $"sha-256=:{encoded}:");
            return true;
        }
        catch
        {
            return false;
        }
    }
}
