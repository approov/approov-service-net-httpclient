using Foundation;
using Security;
using System.Security.Cryptography.X509Certificates;

namespace Approov;

/// <summary>
/// Creates the iOS transport with pinning against the original native
/// <see cref="SecTrust"/> supplied by NSURLSession.
/// </summary>
internal static class ApproovIosHttpMessageHandler
{
    internal static HttpMessageHandler Create()
    {
        // Redirects must re-enter ApproovMessageHandler so each target receives a
        // token/signature for its own URI and stale security headers cannot cross
        // origins.
        var handler = new NSUrlSessionHandler { AllowAutoRedirect = false };

        // Do not use ServerCertificateCustomValidationCallback on iOS. That callback
        // converts SecTrust to a managed X509Chain and applies a separate online
        // revocation policy, which can reject a trust that NSURLSession accepts.
        handler.TrustOverrideForUrl = (_, requestUrl, serverTrust) =>
            ApproovService.VerifyNativeServerTrust(requestUrl, serverTrust);
        return handler;
    }
}

public static partial class ApproovService
{
    /// <summary>
    /// Preserves Apple's native server-trust validation, then applies the current
    /// Approov pins to every certificate in the evaluated native chain.
    /// </summary>
    internal static bool VerifyNativeServerTrust(string requestUrl, SecTrust? serverTrust)
    {
        if (serverTrust == null)
        {
            Log(ApproovLogLevel.Error, "iOS server trust is missing");
            return false;
        }

        try
        {
            // This evaluates the original trust and its SSL hostname policy. It avoids
            // replacing Apple's policy with the stricter managed X509Chain policy used
            // by ServerCertificateCustomValidationCallback.
            bool trusted = serverTrust.Evaluate(out NSError? trustError);
            using (trustError)
            {
                if (!trusted)
                {
                    Log(ApproovLogLevel.Warning,
                        $"iOS server trust validation failed: {trustError?.LocalizedDescription}");
                    return false;
                }
            }

            // NSUrlSessionHandler supplies its original request URL to
            // TrustOverrideForUrl even after an automatic redirect. The hostname in
            // the SSL SecPolicy belongs to the current authentication challenge, so
            // use it for the pin lookup to avoid checking a redirect target against
            // the origin host's pins.
            string? trustHost = GetNativeServerTrustHost(serverTrust);
            if (string.IsNullOrWhiteSpace(trustHost))
            {
                Log(ApproovLogLevel.Error,
                    "iOS server trust does not contain an SSL hostname policy");
                return false;
            }

            if (!Uri.TryCreate(requestUrl, UriKind.Absolute, out _))
            {
                Log(ApproovLogLevel.Error,
                    $"iOS server trust contains an invalid request URL: {requestUrl}");
                return false;
            }

            var chainCertificates = GetNativeChainCertificates(serverTrust);
            try
            {
                if (chainCertificates.Count == 0)
                {
                    Log(ApproovLogLevel.Error,
                        "iOS server trust contains no certificates");
                    return false;
                }

                return VerifyPinsForHost(trustHost, chainCertificates);
            }
            finally
            {
                foreach (var certificate in chainCertificates)
                    certificate.Dispose();
            }
        }
        catch (Exception ex)
        {
            // A trust callback must fail closed. Letting an exception escape from the
            // NSURLSession delegate can otherwise obscure the actual TLS failure.
            Log(ApproovLogLevel.Error,
                $"iOS server trust verification failed: {ex.Message}");
            return false;
        }
    }

    private static string? GetNativeServerTrustHost(SecTrust serverTrust)
    {
        SecPolicy[] policies = serverTrust.GetPolicies();
        try
        {
            foreach (var policy in policies)
            {
                using NSDictionary? properties = policy.GetProperties();
                if (properties?.ObjectForKey(SecPolicyPropertyKey.Name) is NSString host
                    && host.Length > 0)
                    return host.ToString();
            }
        }
        finally
        {
            foreach (var policy in policies)
                policy.Dispose();
        }
        return null;
    }

    private static List<X509Certificate2> GetNativeChainCertificates(SecTrust serverTrust)
    {
        SecCertificate[] nativeCertificates = serverTrust.GetCertificateChain();
        var certificates = new List<X509Certificate2>(nativeCertificates.Length);
        try
        {
            try
            {
                foreach (var nativeCertificate in nativeCertificates)
                    certificates.Add(nativeCertificate.ToX509Certificate2());
                return certificates;
            }
            catch
            {
                foreach (var certificate in certificates)
                    certificate.Dispose();
                throw;
            }
        }
        finally
        {
            foreach (var nativeCertificate in nativeCertificates)
                nativeCertificate.Dispose();
        }
    }
}
