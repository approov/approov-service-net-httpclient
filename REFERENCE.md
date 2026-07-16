# ApproovService API Reference

## Initialization

| Method | Description |
|--------|-------------|
| `ApproovService.Initialize(config, comment?)` | Initialize SDK. Empty `config` = bypass mode. Idempotent for same config. |
| `ApproovService.IsInitialized()` | Returns `true` after successful `Initialize`. |
| `ApproovService.IsApproovEnabled()` | Returns `true` only when the native SDK is active and Approov protection is enabled. Returns `false` in bypass mode (`Initialize("")`) or before initialization. |

## Configuration

| Method | Description |
|--------|-------------|
| `SetApproovTokenHeader(header, prefix)` | Change the token header name/prefix (default: `Approov-Token`, `""`). |
| `SetApproovTraceIDHeader(header?)` | Set optional trace ID header. `null` = disabled. |
| `SetBindingHeader(header?)` | Hash this request header's value into the token. |
| `SetBodyDigestEnabled(bool enabled)` | Enable or disable automatic `Content-Digest` header generation for POST, PUT, and PATCH requests. Enabled by default (but not required). When enabled, the digest is computed and added before signing so signers can include `content-digest` as a signature component. Gracefully skips non-repeatable (one-shot streaming) bodies. Equivalent to `SetBodyDigestEnabled(enabled, false)`: also clears any previously set required mode. |
| `SetBodyDigestEnabled(bool enabled, bool required)` | As above, with opt-in strict enforcement. With `required = true` (and `enabled = true`), a POST/PUT/PATCH request whose body cannot be digested — one-shot streaming content with no computable length, or a digest computation failure — fails closed with a `PermanentException` instead of being sent without a `Content-Digest`. A request with **no body** never fails, even in required mode: no body means there is nothing to digest. `required` is only meaningful while `enabled` is `true`. Like all configuration, this is reset to the default (enabled, not required) by a successful `Initialize`. |
| `SetFailureCacheTTL(seconds)` | Failure cache TTL (default: 5.0 s). |
| `SetLoggingLevel(level)` | `Off/Error/Warning/Info/Debug` (default: `Info`). |
| `SetServiceMutator(mutator)` | Replace callback handler (default: `ApproovServiceMutatorDefault.Shared`). Pass `null` to restore the default fail-closed mutator. |

## Substitution

| Method | Description |
|--------|-------------|
| `AddSubstitutionHeader(header, requiredPrefix?)` | Substitute header value with Approov secure string. |
| `RemoveSubstitutionHeader(header)` | Remove substitution. |
| `AddSubstitutionQueryParam(key)` | Substitute query param value with secure string. |
| `RemoveSubstitutionQueryParam(key)` | Remove substitution. |
| `AddExclusionURLRegex(name, pattern)` | Exclude URLs matching the regex `pattern` from Approov request mutation. `name` is a key used to remove the entry later via `RemoveExclusionURLRegex(name)`. **Note:** this service layer uses a two-argument form (name + pattern); the common interface spec shows a single-argument form. The extra `name` argument is required here because entries are stored in a dictionary keyed by name. |
| `RemoveExclusionURLRegex(name)` | Remove exclusion. |

## SDK operations

| Method | Description |
|--------|-------------|
| `Precheck()` | Fetch token for `approov.io` to validate attestation. |
| `FetchApproovToken(url)` | Fetch token for a URL. |
| `FetchSecureString(key, newDef?)` | Fetch/update a secure string. |
| `FetchCustomJWT(payload)` | Fetch a custom JWT. In bypass mode returns a `Disabled` result without calling the SDK. |
| `GetDeviceID()` | Returns the Approov device ID (`null` in bypass mode). |
| `SetDataHashInToken(data)` | Hash arbitrary data into the token. |
| `SetDevKey(devKey)` | Set a developer key for testing. |
| `FetchConfig()` | Retrieve the latest SDK config string. |
| `GetAccountMessageSignature(message)` | Base64 HMAC-SHA256 signature over `message` using the account key (`null` in bypass mode). |
| `GetInstallMessageSignature(message)` | Base64 ASN.1 DER ES256 signature over `message` using the per-install key (`null` in bypass mode). |

## HTTP integration

| Type | Description |
|------|-------------|
| `ApproovHttpClient` | `HttpClient` subclass wired to `ApproovMessageHandler`. |
| `ApproovMessageHandler` | `DelegatingHandler` calling `UpdateRequestWithApproov`. |
| `ApproovService.VerifyServerTrust(request, serverCert, chain, sslPolicyErrors)` | TLS validation and pinning callback for `ServerCertificateCustomValidationCallback`. |
| `ApproovService.VerifyPinning(request, chainCertificates)` | Applies Approov pinning to an `IReadOnlyList<X509Certificate2>` certificate chain. |
| `ApproovService.UpdateRequestWithApproov(request)` | Core request mutation; returns `ApproovUpdateResponse`. |

## Message signing

Register `ApproovDefaultMessageSigning` as the service mutator to add RFC 9421
`Signature` / `Signature-Input` headers. Requests are signed inside
`HandleInterceptorProcessedRequest`, and only when the request already carries an
Approov token (`ApproovRequestMutations.TokenHeaderKey != null`).

| Type / method | Description |
|---------------|-------------|
| `ApproovDefaultMessageSigning` | Mutator that signs requests. `SetDefaultFactory(f)`, `PutHostFactory(host, f)`. |
| `ApproovDefaultMessageSigning.GenerateDefaultSignatureParametersFactory()` | Default: install signing over `@method`, `@target-uri`, the token/trace-ID headers, optional `Authorization`/`Content-Length`/`Content-Type`, with `created` + 15 s `expires`. |
| `SignatureParametersFactory` | Fluent config: `SetBaseParameters`, `SetUseInstallMessageSigning`/`SetUseAccountMessageSigning`, `SetAddCreated`, `SetExpiresLifetime`, `SetAddApproovTokenHeader`, `SetAddApproovTraceIDHeader`, `AddOptionalHeaders`, `SetBodyDigestConfig`. |

Algorithms and signature ids:

- **install** — `alg="ecdsa-p256-sha256"`; the SDK's base64 ASN.1 DER signature is decoded to
  the raw R‖S (64 byte) form per RFC 9421 §3.3.4.
- **account** — `alg="hmac-sha256"`; the SDK's base64 HMAC is used directly.

Failure contract:

- **Fail-open** — if the SDK cannot provide a signature (`GetInstallMessageSignature` /
  `GetAccountMessageSignature` returns `null`/empty), or no factory is configured, the request
  proceeds without `Signature`/`Signature-Input` headers.
- **Fail-closed** — any exception raised while signing (DER decode error, serialization failure,
  unsupported `alg`, or a missing required `Content-Digest`) propagates as a request failure.
