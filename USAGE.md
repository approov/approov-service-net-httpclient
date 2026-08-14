# Usage

This document describes the features and functionality of the Approov Service for .NET MAUI. It covers how to interact with the service layer and customize its behavior, specifically through `IApproovServiceMutator`. For a basic integration example, refer to the [Quickstart guide](https://github.com/approov/quickstart-maui).

## Initialization

Initialize `ApproovService` once at app startup (for example in `MauiProgram.cs`). Wrap it in a
`try`/`catch`: on success, confirm the layer is actually enabled and record the Approov device ID
together with an app-generated session/correlation id; on failure, log it and continue
**unprotected** by re-initializing with an empty config (bypass mode) so the app still functions.

```csharp
using System;
using System.Diagnostics;
using Approov; // ApproovService

public static class ApproovStartup
{
    // App-generated id for correlating this app session with Approov metrics/logs.
    // Generate once per app run.
    public static readonly string SessionId = Guid.NewGuid().ToString();

    public static void Initialize()
    {
        try
        {
            ApproovService.Initialize("<your-config-string>");

            // Confirm protection is genuinely active before relying on it.
            if (ApproovService.IsApproovEnabled())
            {
                // The Approov device ID is stable per install and useful in support tickets.
                Debug.WriteLine($"Approov enabled. deviceID={ApproovService.GetDeviceID()} session={SessionId}");
            }
            else
            {
                Debug.WriteLine($"Approov initialized in bypass mode. session={SessionId}");
            }
        }
        catch (Exception ex)
        {
            // Never let an initialization failure crash the app. Fall back to bypass mode so
            // the app still functions (unprotected), and surface the failure.
            Debug.WriteLine($"Approov initialization failed: {ex.Message}; continuing unprotected. session={SessionId}");
            ApproovService.Initialize("");
        }
    }
}
```

An empty config string starts **bypass mode** (see below). Comments passed to `Initialize` that
start with `reinit...` or `options:...` are supported and forwarded to the SDK verbatim.

## Bypass Mode (Empty Config)

You can initialize `ApproovService` with an empty configuration string to use the service layer without active Approov protection. This is useful for apps that remotely activate Approov, or when you need a standard `HttpClient` wrapper during development or maintenance:

```csharp
// In MauiProgram.cs — bypass mode
ApproovService.Initialize("");
```

When initialized this way, `ApproovHttpClient` behaves like a standard `HttpClient`. It does not perform token injection, message signing, secure string substitution, or TLS pinning. You can upgrade to full Approov protection later by calling `Initialize` again with a valid configuration string. **Every** successful initialization is a boundary: it resets runtime configuration (token/trace headers, binding, substitutions, exclusions, status-if-no-token) to their defaults and discards any custom service mutator, restoring the default signing mutator. This applies to **every** successful initialization — including a re-initialization with the **same** configuration — matching the reference React Native layer. Re-apply `SetServiceMutator` and any runtime configuration after each initialization.

Use `ApproovService.IsApproovEnabled()` to check at runtime whether Approov is actively protecting requests:

```csharp
if (ApproovService.IsApproovEnabled())
{
    // Full Approov protection active
}
else
{
    // Bypass mode or not yet initialized
}
```

## ApproovServiceMutator

`IApproovServiceMutator` lets you customize the Approov MAUI layer at key points in the request lifecycle. Implement it to override specific callbacks while retaining default behavior for everything else.

### Why use a mutator

- Centralize app-specific policy without forking the service layer.
- Add telemetry on rejections or network failures.
- Skip Approov processing for health checks or local endpoints.
- Adjust behavior when token or secure string fetches fail.

A mutator **cannot** disable TLS pinning in this layer. `HandlePinningShouldProcessRequest`
remains on `IApproovServiceMutator` for source compatibility with the other Approov service
layers, but its return value is **not consulted**: pinning is enforced for every request. This
is a deliberate divergence from the okhttp, retrofit, HttpsUrlConnection and URLSession-family
layers, which do skip pinning for a request when that hook returns `false`.

### Default Behavior

By default, `ApproovService` obtains a signed JWT attestation token and then attempts RFC 9421 installation message signing. The token is typically returned immediately; a network connection to Approov is required on first launch or when the token nears expiry. The default action for each fetch status is:

| Approov Fetch Status | Action | Result |
| :--- | :--- | :--- |
| **Success** | Proceed | Request sent with `Approov-Token`; installation signing is attempted. |
| **No Network / Poor Network / MITM Detected** | Throw `NetworkingErrorException` | Request should be retried. |
| **Rejection** | Throw `RejectionException` | Request rejected; check ARC and reasons. |
| **No Approov Service / Unknown URL / Unprotected URL** | Proceed | Request sent without `Approov-Token`. |

### Customizing Request Handling

Subclass `ApproovServiceMutatorDefault` and override only the methods you need:

```csharp
public class MyMutator : ApproovServiceMutatorDefault
{
    public override bool HandleInterceptorFetchTokenResult(
        IApproovTokenFetchResult result, string url)
    {
        // Allow MITM_DETECTED to pass through instead of throwing
        if (result.Status == ApproovTokenFetchStatus.MitmDetected)
            return false; // proceed without token

        return base.HandleInterceptorFetchTokenResult(result, url);
    }
}
```

Register your mutator at startup:

```csharp
ApproovService.Initialize("<your-config-string>");
ApproovService.SetServiceMutator(new MyMutator());
```

Installing a custom mutator replaces the automatic message-signing mutator. If custom policy must retain signing, subclass `ApproovDefaultMessageSigning`, configure its default factory in the constructor, and override the required virtual callbacks.

## Sending the Fetch Status as a Token

When Approov cannot obtain a token because of a network condition, the default behavior throws a retryable `NetworkingErrorException`; no synthetic HTTP response is created. To let a custom mutator continue and carry the failure status in the token header, enable `SetUseApproovStatusIfNoToken`:

```csharp
ApproovService.SetUseApproovStatusIfNoToken(true);
```

This setting does not override a mutator failure. If the mutator permits processing to continue, the header contains the SDK-style status, such as `NO_NETWORK` or `NO_APPROOV_SERVICE`, and participates in message signing. If the mutator throws or returns `false`, the status is not injected.

The token and trace headers are **omitted** when their values are empty — the layer never sends an empty-valued or prefix-only header. `SetUseApproovStatusIfNoToken` is therefore the intended way to make Approov's outcome visible to the backend when no token is available; without it, a request that proceeds without a usable token simply carries no `Approov-Token` header. This matches the reference React Native layer.

## Token Binding Header

Bind a specific request header's value into the Approov token to tie the token to that credential:

```csharp
ApproovService.SetBindingHeader("Authorization");
```

When the header is present, its complete serialized value is hashed into the token. The backend can verify that the token was issued for that credential. Approov binding is persistent native SDK state: once a `pay` claim has been enabled in a running app it can be changed, but not removed. A request missing the configured header therefore leaves the current binding unchanged. Applications should configure automatic binding only for a header that is consistently present throughout the protected session.

For manual binding outside automatic request processing:

```csharp
ApproovService.SetDataHashInToken("order-123");
var tokenResult = ApproovService.FetchApproovToken("https://api.example.com/orders");
```

Do not combine manual `SetDataHashInToken` calls with `SetBindingHeader`. Both use the same persistent native SDK state, and automatic request processing replaces that state whenever the configured header is present.

## Secure String Substitution

Replace sensitive header values with Approov-managed secrets at runtime:

```csharp
// Register the header for substitution
ApproovService.AddSubstitutionHeader("X-Api-Key", null);

// The placeholder in the request header is replaced automatically
var req = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/data");
req.Headers.Add("X-Api-Key", "your-placeholder");
await client.SendAsync(req); // X-Api-Key is replaced with the live secret
```

Register query parameters for substitution with `AddSubstitutionQueryParam`. Remove registrations with `RemoveSubstitutionHeader` / `RemoveSubstitutionQueryParam`.

Use `AddExclusionURLRegex(pattern)` to exclude URLs from substitution (e.g. health check endpoints). The named `AddExclusionURLRegex(name, pattern)` compatibility overload is also available.

## HTTP Message Signing

HTTP message signing is provided by `ApproovDefaultMessageSigning`, which is installed automatically
as the initial service mutator. It adds RFC 9421 `Signature` / `Signature-Input` headers to requests that already
carry an Approov token (requests without a token are never signed). Two modes are supported:

- **Install signing** — `alg="ecdsa-p256-sha256"`, signature id `install` (the default). Signed
  with the per-install device key. The SDK returns an ASN.1 DER signature, which is emitted as the
  raw R‖S (64 byte) form required by RFC 9421 §3.3.4.
- **Account signing** — `alg="hmac-sha256"`, signature id `account`. Signed with the account key.

The default configuration requires no additional registration. It uses install signing over
`@method`, `@target-uri`, the Approov token header and trace-ID header, optional
`Authorization`/`Content-Length`/`Content-Type` when present, plus `created` and a 15-second
`expires`:

```csharp
ApproovService.Initialize("<your-config-string>");
var client = new ApproovHttpClient();
```

Customize what is covered with a `SignatureParametersFactory`, and vary it per host:

```csharp
var baseParams = new SignatureParameters();
baseParams.AddComponentIdentifier(new StringItem("@method"));
baseParams.AddComponentIdentifier(new StringItem("@target-uri"));

var factory = new ApproovDefaultMessageSigning.SignatureParametersFactory()
    .SetBaseParameters(baseParams)
    .SetUseInstallMessageSigning()             // or .SetUseAccountMessageSigning()
    .SetAddCreated(true)
    .SetExpiresLifetime(15)
    .SetAddApproovTokenHeader(true)
    .SetAddApproovTraceIDHeader(true)
    .AddOptionalHeaders("Authorization", "Content-Type")
    .SetBodyDigestConfig(ApproovDefaultMessageSigning.DIGEST_SHA256, false);

ApproovService.SetServiceMutator(
    new ApproovDefaultMessageSigning()
        .SetDefaultFactory(factory)
        .PutHostFactory("api.example.com", otherFactory));
```

Signing is **fail-open** for operational failures: if the SDK cannot provide a signature or throws, or Base64/DER conversion or signature serialization fails, the error is logged and the request proceeds without signature headers. Unsupported algorithms and failure to create an explicitly required body digest remain fail-closed.

> **Initialization resets the mutator.** Every successful `ApproovService.Initialize` call,
> including one made with the same configuration, discards a custom mutator and restores the
> default, along with the token header, trace header, binding header, substitutions and
> exclusion regexes. Apply your configuration *after* initializing, and reapply it if you
> initialize again. A discarded custom mutator or binding header is logged at warning level.
> This matches the React Native service layer, where a custom mutator participates in
> rejection and substitution decisions and so must not outlive the initialization it was
> scoped to.

Calling `SetServiceMutator(null)` restores a newly configured default signing mutator. To disable automatic signing explicitly, install the base mutator:

```csharp
ApproovService.SetServiceMutator(ApproovServiceMutatorDefault.Shared);
```

Redirects re-enter token and signing processing for the target URI. Secure-string substitution is performed only on the initial request so an already-resolved secret is not used as a second lookup key. On a cross-origin redirect, authorization, binding, cookie, substitution headers, and configured substitution query parameters are removed conservatively—even when the target `Location` explicitly contains one of those query-parameter names.

## Body Digest

The automatically installed signer computes a SHA-256 `Content-Digest` for a protected request with a non-empty, replayable body and covers it in the signature:

```csharp
// Enabled by default — disable if not needed
ApproovService.SetBodyDigestEnabled(false);
```

Digest generation occurs inside the signer, so bypassed and unprotected requests are not modified. Required mode can be selected with `SetBodyDigestEnabled(true, required: true)`; a missing, unknown-length (no `Content-Length`, e.g. a streamed or chunked body), or otherwise non-replayable body then fails the request. A **zero-length** body is not a failure: it has a known length and produces the valid digest of an empty payload, per RFC 9421. When using a custom signing factory, configure its digest directly with `SetBodyDigestConfig`.

## TLS Certificate Pinning

`ApproovMessageHandler` wires TLS pinning automatically. Android preserves the platform callback's certificate validation and checks the complete peer chain. iOS evaluates the original native `SecTrust` and then checks the complete native chain, avoiding a second managed revocation policy. Pins are managed by the Approov cloud and updated dynamically.

If you supply your own handler, `ApproovMessageHandler` installs pinning on it for you —
`HttpClientHandler`, `SocketsHttpHandler`, `AndroidMessageHandler` and `NSUrlSessionHandler`
are all covered. You do not need to wire anything by hand:

```csharp
handler.AllowAutoRedirect = false;   // done for you as well, for supported handler types
handler.ServerCertificateCustomValidationCallback =
    (message, cert, chain, errors) =>
        ApproovService.VerifyServerTrust(message, cert, chain, errors);
```

**A certificate callback of your own does not replace pinning.** If the handler already carries
one, Approov pinning is **composed in front of it**: pinning runs first and short-circuits, so a
pin mismatch rejects the connection without ever consulting your callback, and your callback can
only further restrict what pinning already accepted. The hand-wired snippet above therefore still
works (`VerifyServerTrust` is a pure function of the certificate, chain and policy errors, so
re-running it is harmless), but a permissive callback such as `(_, _, _, _) => true` can no
longer disable pin enforcement. A composed callback is logged at warning level.

The default constructor is recommended on iOS because it has access to the original native trust object. The custom-handler constructor disables redirects for supported platform handlers and rejects handlers whose redirect behavior cannot be controlled. For another terminal-handler type, first disable its redirects and use `new ApproovMessageHandler(handler, automaticRedirectsAlreadyDisabled: true)` to acknowledge that security requirement explicitly.

That flag acknowledges **redirects only, not pinning**. Pinning is installed by both constructors, and on the terminal handler of a `DelegatingHandler` chain, whenever the terminal type exposes a certificate callback. If it does not — a hand-written `HttpMessageHandler`, a test double, a transport of your own — the layer cannot pin it and says so at **error** level: requests through that handler are tokenized and signed but *not* pin-checked by the service layer, and you must enforce the Approov pins inside the handler yourself (`ApproovService.VerifyServerTrust` is public for exactly this).

## Failure Cache

To avoid blocking requests during brief outages, `ApproovService` caches recent network-failure results. The default TTL is 5 seconds:

```csharp
ApproovService.SetFailureCacheTTL(10.0); // seconds
```

Set to 0 to disable caching.

## Logging

```csharp
ApproovService.SetLoggingLevel(ApproovLogLevel.Debug);
```

Levels: `Off`, `Error`, `Warning`, `Info` (default), `Debug`. A message is emitted only when its
level is at or above the configured level; `Off` suppresses all output.

`SetLoggingLevel` governs **this service layer's own logging** (initialization, request mutation,
and every fail-open path), which is emitted through a platform sink (`android.util.Log` on Android,
`NSLog`/`os_log` on iOS) that survives release builds. Its scope is the service layer only: the
underlying native Approov SDK manages its own internal logging and exposes no log-level control to
the layer, so `SetLoggingLevel` does not change SDK-internal verbosity. This matches the logging
contract of the other Approov service layers (for example React Native), where the same call gates
the wrapper's logging rather than the SDK's.

## Platform Requirements

| | Android | iOS |
|---|---|---|
| Minimum version | API 23 (Android 6.0) | iOS 15.0 |
| Target framework | `net9.0-android` | `net9.0-ios` |
| SDK artifact | `libs/approov.aar` | `iOS.Binding/libs/Approov.xcframework` |
| Manifest / plist changes | `INTERNET` **and** `ACCESS_NETWORK_STATE` permissions | none |

`ACCESS_NETWORK_STATE` is required. Without it the SDK cannot query connectivity and every token
fetch fails as `InternalError`, even though `Initialize`, `GetDeviceID()` and message signing all
appear to work. See the [README](README.md) for how to fetch and place the SDK binaries.

## Real-world Examples

### Policy-driven mutator: host scoping and offline fallback

A single mutator that skips Approov processing for endpoints that do not need it, and lets requests
proceed when the device is genuinely offline rather than failing the user's action:

```csharp
using Approov;              // ApproovService, IApproovTokenFetchResult, ApproovTokenFetchStatus
using Approov.Util.Sig;     // ApproovDefaultMessageSigning

public sealed class AppPolicyMutator : ApproovDefaultMessageSigning
{
    private static readonly HashSet<string> UnprotectedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "health.example.com", "cdn.example.com"
    };

    // Skip Approov entirely for hosts that do not need it.
    public override bool HandleInterceptorShouldProcessRequest(HttpRequestMessage request)
        => request.RequestUri is not { } uri || !UnprotectedHosts.Contains(uri.Host);

    // Send unprocessed on genuine network failures; keep every other status fail-closed.
    // Tokens only: this host set uses no secret substitution. See the warning below.
    public override bool HandleInterceptorFetchTokenResult(IApproovTokenFetchResult result, string url)
        => result.Status switch
        {
            ApproovTokenFetchStatus.NoNetwork or ApproovTokenFetchStatus.PoorNetwork => false,
            _ => base.HandleInterceptorFetchTokenResult(result, url)
        };
}

// After every successful Initialize, because initialization discards custom mutators:
ApproovService.SetServiceMutator(new AppPolicyMutator());
```

Returning `false` from `HandleInterceptorFetchTokenResult` sends the request unprocessed. Throwing
(the default for most failure statuses) aborts the request.

`false` does more than omit the Approov token. The layer stops at that point, so the request also
goes out without the trace ID header, without header or query parameter secret substitution, and
without an RFC 9421 message signature.

> **If you use Secrets Protection, do not return `false` here.** The request carries the
> **placeholder** value instead of the real secret. The third-party API rejects the call, and the
> placeholder is disclosed to it. Return `false` only when every host this mutator handles relies on
> Approov tokens alone.

A request that proceeds this way is unprotected, so the backend must remain the enforcement point.

### Log rejections with ARC and device ID to your telemetry

Monitoring rejections is a key part of your security strategy. Ideally your backend includes the
**ARC (Approov Rejection Code)** and **device ID** in its error responses when it rejects a request,
and you log those.

**Why server-side values are preferred:**

1. **Avoid misleading network events.** A call to `GetLastARC()` can trigger a background network
   event that completes a delayed attestation, returning an ARC from a *successful* attestation that
   happened *after* your request failed.
2. **Corporate firewall and MITM cases.** If your mutator lets a request proceed on `MitmDetected`,
   the request goes out without a token and no rejection code exists yet for that attempt.
3. **Accuracy and correlation.** Logging the ARC the server actually rejected on gives perfect
   correlation in your dashboards.

```csharp
using HttpResponseMessage response = await client.SendAsync(request);
if (!response.IsSuccessStatusCode)
{
    // Preferred: the ARC your own backend observed and rejected on.
    string? serverArc = response.Headers.TryGetValues("X-Approov-Error-ARC", out var values)
        ? values.FirstOrDefault()
        : null;

    // Fallback only if the server cannot supply it; may trigger background network events.
    string? arc = serverArc ?? ApproovService.GetLastARC();
    string? deviceId = ApproovService.GetDeviceID();
    Console.WriteLine($"Request rejected. ARC={arc} deviceID={deviceId}");
}
```

A `RejectionException` also carries `ARC` and `RejectionReasons` directly, for the paths where the
layer aborts the request rather than letting it reach your backend.

## Tips

- Keep mutator logic fast and side-effect safe. These hooks run on the request path.
- Subclass `ApproovDefaultMessageSigning` to keep message signing and layer your changes on top;
  subclass `ApproovServiceMutatorDefault` (or install `ApproovServiceMutatorDefault.Shared`) when you
  want no signing.
- If you override multiple hooks, keep them focused — one concern per hook — for easier testing.
- Re-apply `SetServiceMutator` and any runtime configuration after **every** successful
  `Initialize`, including a re-initialization with the same config.
- Use the asynchronous API. Synchronous `HttpClient.Send` throws `NotSupportedException` rather than
  sending an unprotected request.
- A mutator cannot disable TLS pinning in this layer; see the divergence table in
  [REFERENCE.md](REFERENCE.md).
