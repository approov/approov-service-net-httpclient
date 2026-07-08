# Usage

This document describes the features and functionality of the Approov Service for .NET MAUI. It covers how to interact with the service layer and customize its behavior, specifically through `IApproovServiceMutator`. For a basic integration example, refer to the [Quickstart guide](https://github.com/approov/quickstart-maui).

## Bypass Mode (Empty Config)

You can initialize `ApproovService` with an empty configuration string to use the service layer without active Approov protection. This is useful for apps that remotely activate Approov, or when you need a standard `HttpClient` wrapper during development or maintenance:

```csharp
// In MauiProgram.cs — bypass mode
ApproovService.Initialize("");
```

When initialized this way, `ApproovHttpClient` behaves like a standard `HttpClient`. It does not perform token injection, message signing, secure string substitution, or TLS pinning. You can upgrade to full Approov protection later in the application lifecycle by calling `Initialize` again with a valid configuration string — the service preserves all other settings during the upgrade.

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
- Customize pinning decisions per request.
- Adjust behavior when token or secure string fetches fail.

### Default Behavior

By default, `ApproovService` processes requests using the Approov SDK to obtain a signed JWT attestation token. The token is typically returned immediately; a network connection to Approov is required on first launch or when the token nears expiry. The default action for each fetch status is:

| Approov Fetch Status | Action | Result |
| :--- | :--- | :--- |
| **Success** | Proceed | Request sent with `Approov-Token` header. |
| **No Network / Poor Network / MITM Detected** | Throw `NetworkingErrorException` | Request should be retried. |
| **Rejection** | Throw `RejectionException` | Request rejected; check ARC and reasons. |
| **No Approov Service / Unknown URL / Unprotected URL** | Proceed | Request sent without `Approov-Token`. |

### Customizing Request Handling

Subclass `ApproovServiceMutatorDefault` and override the methods you need:

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

## Sending the Fetch Status as a Token

When Approov cannot obtain a token (e.g. `NoNetwork`), the default behavior throws a `NetworkingErrorException` causing `ApproovMessageHandler` to return HTTP 503. If you prefer the request to proceed and carry the failure reason in the token header, enable `SetUseApproovStatusIfNoToken`:

```csharp
ApproovService.SetUseApproovStatusIfNoToken(true);
```

With this enabled, on network failure the header will contain a value like `"NoNetwork"` instead of a real token. Your backend can then decide how to handle it. This setting works together with a custom mutator — if a mutator throws for a given status, the status string is still injected and the request proceeds.

## Token Binding Header

Bind a specific request header's value into the Approov token to tie the token to that credential:

```csharp
ApproovService.SetBindingHeader("Authorization");
```

On each request, the value of the `Authorization` header is hashed into the token. The backend can verify that the token was issued for that specific credential. Requests without the binding header proceed normally without the hash.

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

Use `AddExclusionURLRegex(name, pattern)` to exclude URLs from substitution (e.g. health check endpoints).

## HTTP Message Signing

HTTP message signing is provided by `ApproovDefaultMessageSigning`, registered as the service
mutator. It adds RFC 9421 `Signature` / `Signature-Input` headers to every request that already
carries an Approov token (requests without a token are never signed). Two modes are supported:

- **Install signing** — `alg="ecdsa-p256-sha256"`, signature id `install` (the default). Signed
  with the per-install device key. The SDK returns an ASN.1 DER signature, which is emitted as the
  raw R‖S (64 byte) form required by RFC 9421 §3.3.4.
- **Account signing** — `alg="hmac-sha256"`, signature id `account`. Signed with the account key.

Register the default configuration — install signing over `@method`, `@target-uri`, the Approov
token header and trace-ID header, optional `Authorization`/`Content-Length`/`Content-Type` when
present, plus `created` and a 15-second `expires`:

```csharp
ApproovService.Initialize("<your-config-string>");
ApproovService.SetServiceMutator(
    new ApproovDefaultMessageSigning().SetDefaultFactory(
        ApproovDefaultMessageSigning.GenerateDefaultSignatureParametersFactory()));
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

Signing is **fail-open**: if the SDK cannot provide a signature the request proceeds unsigned.

## Body Digest

When message signing is active, the `Content-Digest` header is automatically computed for POST, PUT, and PATCH requests (SHA-256 of the body in `sha-256=:<base64>:` format) and added before signing, so signers can include `content-digest` as a component:

```csharp
// Enabled by default — disable if not needed
ApproovService.SetBodyDigestEnabled(false);
```

Body digest is computed asynchronously in `ApproovMessageHandler.SendAsync` before `UpdateRequestWithApproov` is called, preserving the synchronous contract of the core service.

## TLS Certificate Pinning

`ApproovMessageHandler` wires TLS pinning automatically via `ServerCertificateCustomValidationCallback`. The pins are managed by the Approov cloud and updated dynamically — no app update required when pins rotate.

If you build a custom `HttpMessageHandler`, call `ApproovService.VerifyPinning` from your certificate validation callback:

```csharp
handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) =>
{
    if (cert == null) return false;
    using var x509 = new X509Certificate2(cert.RawData);
    return ApproovService.VerifyPinning(message, x509);
};
```

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

Levels: `Off`, `Error`, `Warning`, `Info` (default), `Debug`.
