// ApproovService.MAUI/util/sig/BodyDigest.cs
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace Approov;

// Outcome of a Content-Digest attempt, distinguishing "nothing to digest" from
// "there is a body but it cannot be digested" so required mode can fail closed
// on the latter only
internal enum BodyDigestOutcome
{
    // Method carries no digestable body (not POST/PUT/PATCH) or there is no body at
    // all: never an error, even in required mode — there is nothing to digest
    NotApplicable,
    // Content-Digest header computed and added
    Added,
    // A body is present but cannot be digested: one-shot streaming content (unknown
    // length, cannot be buffered and replayed) or the digest computation failed
    CannotDigest
}

internal static class BodyDigest
{
    private static readonly System.Collections.Generic.HashSet<HttpMethod> _digestMethods =
        new() { HttpMethod.Post, HttpMethod.Put, HttpMethod.Patch };

    internal static async Task<BodyDigestOutcome> TryAddAsync(HttpRequestMessage request)
    {
        if (!_digestMethods.Contains(request.Method)) return BodyDigestOutcome.NotApplicable;
        if (request.Content == null) return BodyDigestOutcome.NotApplicable;
        // One-shot streaming content has no computable length (e.g. StreamContent over
        // a non-seekable stream): it is potentially unbounded and cannot be replayed,
        // so it is treated as non-digestable rather than buffered wholesale — skipped
        // in default mode, failed closed in required mode
        if (request.Content.Headers.ContentLength == null) return BodyDigestOutcome.CannotDigest;
        try
        {
            await request.Content.LoadIntoBufferAsync();
            byte[] body = await request.Content.ReadAsByteArrayAsync();
            byte[] hash = SHA256.HashData(body);
            string encoded = System.Convert.ToBase64String(hash);
            request.Content.Headers.Remove("Content-Digest");
            request.Content.Headers.TryAddWithoutValidation(
                "Content-Digest", $"sha-256=:{encoded}:");
            return BodyDigestOutcome.Added;
        }
        catch
        {
            return BodyDigestOutcome.CannotDigest;
        }
    }
}
