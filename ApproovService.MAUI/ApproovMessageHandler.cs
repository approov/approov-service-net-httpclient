using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Approov;

public class ApproovMessageHandler : DelegatingHandler
{
    private const int MaximumAutomaticRedirects = 50;

    public ApproovMessageHandler() : base(CreatePlatformHandler()) { }
    public ApproovMessageHandler(HttpMessageHandler inner)
        : base(DisableInnerAutomaticRedirects(inner)) { }

    // Explicit escape hatch for custom terminal handlers whose redirect behavior cannot
    // be inspected. Callers must only set this to true after disabling redirects on the
    // entire inner chain, so every target re-enters the Approov pipeline.
    public ApproovMessageHandler(HttpMessageHandler inner,
        bool automaticRedirectsAlreadyDisabled)
        : base(automaticRedirectsAlreadyDisabled
            ? inner
            : throw new ArgumentException(
                "The inner handler must have automatic redirects disabled", nameof(inner)))
    { }

    // DelegatingHandler.Send forwards straight to the inner handler. Inheriting it would let
    // any synchronous caller (HttpClient.Send, added in .NET 5) reach the network with no
    // Approov token, no message signature and no secure-string substitution, so placeholder
    // values would be transmitted verbatim while the connection is still pinned: a silent
    // bypass. Supporting it properly would require a second copy of the redirect loop and
    // would block the caller's thread on attestation, so this fails loudly instead.
    protected override HttpResponseMessage Send(
        HttpRequestMessage request, CancellationToken cancellationToken)
        => throw new NotSupportedException(
            "ApproovMessageHandler does not support synchronous HttpClient.Send. Use the "
            + "asynchronous API (SendAsync, GetAsync, PostAsync) so the request receives its "
            + "Approov token, secure string substitutions and message signature.");

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpRequestMessage currentRequest = request;
        HttpRequestMessage? ownedRedirectRequest = null;
        try
        {
            for (int redirectCount = 0; ; redirectCount++)
            {
                var sent = await SendOnceWithApproovAsync(
                    currentRequest, cancellationToken).ConfigureAwait(false);
                HttpResponseMessage response = sent.Response;

                if (redirectCount >= MaximumAutomaticRedirects
                    || !TryGetRedirectUri(response, sent.Request, out Uri redirectUri))
                    return response;

                HttpRequestMessage redirectedRequest;
                try
                {
                    redirectedRequest = await CreateRedirectRequestAsync(
                        sent.Request, redirectUri, response.StatusCode,
                        cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    response.Dispose();
                    throw;
                }

                // Intermediate responses are transport details, just as with the native
                // automatic redirect handlers. The next request re-enters the full Approov
                // token/substitution/signing flow for its effective URI.
                response.Dispose();
                ownedRedirectRequest?.Dispose();
                ownedRedirectRequest = redirectedRequest;
                currentRequest = redirectedRequest;
            }
        }
        catch
        {
            // Intermediate generated requests are owned by this handler. The final
            // generated request remains alive through response.RequestMessage so callers
            // can inspect it for the response lifetime.
            ownedRedirectRequest?.Dispose();
            throw;
        }
    }

    private async Task<SentRequest> SendOnceWithApproovAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var update = ApproovService.UpdateRequestWithApproov(request);

        switch (update.Decision)
        {
            case ApproovFetchDecision.ShouldIgnore:
            case ApproovFetchDecision.ShouldProceed:
            {
                HttpRequestMessage updatedRequest = update.Request!;
                HttpResponseMessage response = await base.SendAsync(
                    updatedRequest, cancellationToken).ConfigureAwait(false);
                response.RequestMessage ??= updatedRequest;
                return new SentRequest(response, updatedRequest);
            }

            case ApproovFetchDecision.ShouldRetry:
                if (update.Error != null) throw update.Error;
                throw new NetworkingErrorException(
                    update.SdkMessage ?? "Approov request should be retried");

            case ApproovFetchDecision.ShouldFail:
            default:
                if (update.Error != null) throw update.Error;
                throw new ApproovException(update.SdkMessage ?? "Approov request rejected");
        }
    }

    private static bool TryGetRedirectUri(HttpResponseMessage response,
        HttpRequestMessage request, out Uri redirectUri)
    {
        redirectUri = null!;
        int status = (int)response.StatusCode;
        if (status is not (301 or 302 or 303 or 307 or 308)
            || response.Headers.Location == null
            || request.RequestUri == null)
            return false;

        redirectUri = response.Headers.Location.IsAbsoluteUri
            ? response.Headers.Location
            : new Uri(request.RequestUri, response.Headers.Location);
        if (redirectUri.Scheme != Uri.UriSchemeHttp
            && redirectUri.Scheme != Uri.UriSchemeHttps)
            return false;

        // Match HttpClient's secure default: never automatically downgrade HTTPS.
        if (request.RequestUri.Scheme == Uri.UriSchemeHttps
            && redirectUri.Scheme == Uri.UriSchemeHttp)
            return false;
        return true;
    }

    private static async Task<HttpRequestMessage> CreateRedirectRequestAsync(
        HttpRequestMessage previous, Uri redirectUri, HttpStatusCode statusCode,
        CancellationToken cancellationToken)
    {
        bool rewriteToGet = (statusCode == HttpStatusCode.SeeOther
                && previous.Method != HttpMethod.Head)
            || ((statusCode == HttpStatusCode.MovedPermanently
                    || statusCode == HttpStatusCode.Found)
                && previous.Method == HttpMethod.Post);
        HttpMethod method = rewriteToGet ? HttpMethod.Get : previous.Method;
        bool sameOrigin = previous.RequestUri != null
            && IsSameOrigin(previous.RequestUri, redirectUri);
        if (!sameOrigin)
            redirectUri = RemoveSubstitutionQueryParameters(redirectUri);

        var redirected = new HttpRequestMessage(method, redirectUri)
        {
            Version = previous.Version,
            VersionPolicy = previous.VersionPolicy
        };
        foreach (var option in previous.Options)
            redirected.Options.Set(
                new HttpRequestOptionsKey<object?>(option.Key), option.Value);
        redirected.Options.Set(ApproovService.SkipSubstitutionsOption, true);

        var (tokenHeader, _) = ApproovService.GetApproovTokenHeader();
        string? traceHeader = ApproovService.GetApproovTraceIDHeader();
        var strippedHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            tokenHeader,
            "Signature",
            "Signature-Input",
            "Signature-Base-Digest",
            "Host"
        };
        if (traceHeader != null) strippedHeaders.Add(traceHeader);
        if (rewriteToGet)
        {
            strippedHeaders.Add("Transfer-Encoding");
            strippedHeaders.Add("Expect");
        }
        if (!sameOrigin)
        {
            strippedHeaders.Add("Authorization");
            strippedHeaders.Add("Proxy-Authorization");
            strippedHeaders.Add("Cookie");
            string? bindingHeader = ApproovService.GetBindingHeader();
            if (bindingHeader != null) strippedHeaders.Add(bindingHeader);
            foreach (string substitutionHeader in
                ApproovService.GetSubstitutionHeaders().Keys)
                strippedHeaders.Add(substitutionHeader);
        }

        foreach (var header in previous.Headers)
            if (!strippedHeaders.Contains(header.Key))
                redirected.Headers.TryAddWithoutValidation(header.Key, header.Value);

        if (!rewriteToGet && previous.Content != null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // ByteArrayContent and its framework subclasses (StringContent and
            // FormUrlEncodedContent) remain repeatable after transport. Do not eagerly
            // buffer arbitrary known-length content: that would turn large uploads into
            // unbounded memory use merely because redirects are enabled.
            if (previous.Content is not ByteArrayContent)
                throw new HttpRequestException(
                    "The redirected request body is not replayable");

            byte[] body;
            try
            {
                body = await previous.Content.ReadAsByteArrayAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new HttpRequestException(
                    "The redirected request body could not be replayed", ex);
            }

            var content = new ByteArrayContent(body);
            foreach (var header in previous.Content.Headers)
                if (!header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)
                    && !header.Key.Equals("Content-Digest", StringComparison.OrdinalIgnoreCase))
                    content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            redirected.Content = content;
        }

        return redirected;
    }

    private static Uri RemoveSubstitutionQueryParameters(Uri uri)
    {
        HashSet<string> substitutionKeys = ApproovService.GetSubstitutionQueryParams();
        if (substitutionKeys.Count == 0 || string.IsNullOrEmpty(uri.Query)) return uri;

        string rawQuery = uri.Query.TrimStart('?');
        string[] retained = rawQuery.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(part =>
            {
                string encodedName = part.Split('=', 2)[0];
                return !substitutionKeys.Contains(Uri.UnescapeDataString(encodedName));
            })
            .ToArray();
        if (retained.Length == rawQuery.Split('&',
                StringSplitOptions.RemoveEmptyEntries).Length)
            return uri;

        return new UriBuilder(uri) { Query = string.Join("&", retained) }.Uri;
    }

    private static bool IsSameOrigin(Uri first, Uri second)
        => string.Equals(first.Scheme, second.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(first.IdnHost, second.IdnHost,
                StringComparison.OrdinalIgnoreCase)
            && first.Port == second.Port;

    private sealed record SentRequest(HttpResponseMessage Response,
        HttpRequestMessage Request);

    private static HttpMessageHandler CreatePlatformHandler()
    {
#if IOS
        return ApproovIosHttpMessageHandler.Create();
#else
        var handler = new HttpClientHandler { AllowAutoRedirect = false };
        handler.ServerCertificateCustomValidationCallback =
            (message, cert, chain, errors) =>
                ApproovService.VerifyServerTrust(message, cert, chain, errors);
        return handler;
#endif
    }

    // The parameterless constructor installs Approov pinning via CreatePlatformHandler, but
    // the constructors that accept a caller-supplied handler previously installed none, so
    // requests carried a valid token and signature over an unpinned connection with no error
    // and no log.
    //
    // A caller-supplied callback is composed with Approov pinning rather than left in place:
    // skipping installation meant a permissive callback (including `(_, _, _, _) => true`)
    // silently disabled pin enforcement while requests were still tokenized and signed.
    // Approov pinning always runs first and short-circuits, so a pin failure rejects the
    // connection without consulting the caller's callback. The callback documented in
    // USAGE.md keeps working: it re-runs VerifyServerTrust, which is a pure function of the
    // supplied certificate, chain and policy errors.
    private static void EnsurePinning(HttpClientHandler handler)
    {
        var existing = handler.ServerCertificateCustomValidationCallback;
        if (existing != null)
            ApproovService.Log(ApproovLogLevel.Warning,
                "caller-supplied ServerCertificateCustomValidationCallback found; "
                + "Approov pinning now runs before it and both must accept the connection");
        handler.ServerCertificateCustomValidationCallback =
            (message, cert, chain, errors) =>
                ApproovService.VerifyServerTrust(message, cert, chain, errors)
                && (existing == null || existing(message, cert, chain, errors));
    }

    // SocketsHttpHandler validates through SslOptions.RemoteCertificateValidationCallback,
    // which is handed the SslStream rather than the HttpRequestMessage. Its TargetHostName
    // carries the SNI host, which is what pinning is keyed by, so pinning is installable here
    // too. Verified against the framework rather than assumed.
    private static void EnsurePinning(SocketsHttpHandler handler)
    {
        var existing = handler.SslOptions.RemoteCertificateValidationCallback;
        if (existing != null)
            ApproovService.Log(ApproovLogLevel.Warning,
                "caller-supplied SslOptions.RemoteCertificateValidationCallback found; "
                + "Approov pinning now runs before it and both must accept the connection");
        handler.SslOptions.RemoteCertificateValidationCallback =
            (sender, cert, chain, errors) =>
                ApproovService.VerifyServerTrustForStream(sender, cert, chain, errors)
                && (existing == null || existing(sender, cert, chain, errors));
    }

#if ANDROID
    // AndroidMessageHandler is the default MAUI transport on Android and terminates its own
    // chain, so it reached this switch with redirects disabled and no pinning at all: the
    // same hole the HttpClientHandler/SocketsHttpHandler cases were fixed for. It exposes the
    // request-aware callback, so the identical composition applies.
    private static void EnsurePinning(Xamarin.Android.Net.AndroidMessageHandler handler)
    {
        var existing = handler.ServerCertificateCustomValidationCallback;
        if (existing != null)
            ApproovService.Log(ApproovLogLevel.Warning,
                "caller-supplied AndroidMessageHandler certificate callback found; "
                + "Approov pinning now runs before it and both must accept the connection");
        handler.ServerCertificateCustomValidationCallback =
            (message, cert, chain, errors) =>
                ApproovService.VerifyServerTrust(message, cert, chain, errors)
                && (existing == null || existing(message, cert, chain, errors));
    }
#endif

#if IOS
    // A caller-supplied NSUrlSessionHandler also received no pinning. Use TrustOverrideForUrl
    // rather than ServerCertificateCustomValidationCallback for the reason documented in
    // ApproovIosHttpMessageHandler: it evaluates the original native SecTrust instead of
    // applying the stricter managed X509Chain policy.
    private static void EnsurePinning(NSUrlSessionHandler handler)
    {
        var existing = handler.TrustOverrideForUrl;
        if (existing != null)
            ApproovService.Log(ApproovLogLevel.Warning,
                "caller-supplied NSUrlSessionHandler.TrustOverrideForUrl found; "
                + "Approov pinning now runs before it and both must accept the connection");
        handler.TrustOverrideForUrl = (sender, requestUrl, serverTrust) =>
            ApproovService.VerifyNativeServerTrust(requestUrl, serverTrust)
            && (existing == null || existing(sender, requestUrl, serverTrust));
    }
#endif

    private static HttpMessageHandler DisableInnerAutomaticRedirects(
        HttpMessageHandler inner)
    {
        ArgumentNullException.ThrowIfNull(inner);

        HttpMessageHandler terminal = inner;
        while (terminal is DelegatingHandler delegating
            && delegating.InnerHandler != null)
            terminal = delegating.InnerHandler;

        switch (terminal)
        {
            case HttpClientHandler httpClientHandler:
                httpClientHandler.AllowAutoRedirect = false;
                EnsurePinning(httpClientHandler);
                return inner;
            case SocketsHttpHandler socketsHttpHandler:
                socketsHttpHandler.AllowAutoRedirect = false;
                EnsurePinning(socketsHttpHandler);
                return inner;
#if ANDROID
            case Xamarin.Android.Net.AndroidMessageHandler androidMessageHandler:
                androidMessageHandler.AllowAutoRedirect = false;
                EnsurePinning(androidMessageHandler);
                return inner;
#endif
#if IOS
            case NSUrlSessionHandler urlSessionHandler:
                urlSessionHandler.AllowAutoRedirect = false;
                EnsurePinning(urlSessionHandler);
                return inner;
#endif
            default:
                throw new ArgumentException(
                    "The custom handler must terminate in HttpClientHandler, "
                    + "SocketsHttpHandler, or NSUrlSessionHandler so automatic "
                    + "redirects can be disabled safely.", nameof(inner));
        }
    }
}
