# ApproovService API Reference

## Initialization

| Method | Description |
|--------|-------------|
| `ApproovService.Initialize(config, comment?)` | Initialize SDK. Empty `config` enters bypass mode only before a valid initialization. Repeating the same non-empty config preserves runtime settings; changing config restores request settings but preserves the active mutator. |
| `ApproovService.IsInitialized()` | Returns `true` after successful `Initialize`. |
| `ApproovService.IsApproovEnabled()` | Returns `true` only when the native SDK is active and Approov protection is enabled. Returns `false` in bypass mode (`Initialize("")`) or before initialization. |

## Configuration

| Method | Description |
|--------|-------------|
| `SetApproovTokenHeader(header, prefix)` | Change the token header name/prefix (default: `Approov-Token`, `""`). |
| `SetApproovTraceIDHeader(header?)` | Set the trace ID header (default: `Approov-TraceID`). `null` disables it. |
| `SetBindingHeader(header?)` | Hash this request header's serialized value into the token when present. A missing header does not change the SDK's persistent binding value. |
| `SetBodyDigestEnabled(bool enabled)` | Configure SHA-256 `Content-Digest` generation in the automatically installed signer. Enabled and optional by default. Equivalent to `SetBodyDigestEnabled(enabled, false)`. Custom signing factories must use `SetBodyDigestConfig` directly. |
| `SetBodyDigestEnabled(bool enabled, bool required)` | As above, with strict mode. If enabled and required, failure to generate a digest—including a missing, empty, unknown-length, or non-replayable body—fails the request. Every successful initialization restores the enabled/optional defaults, including a same-config one. |
| `SetFailureCacheTTL(seconds)` | Failure cache TTL (default: 5.0 s). |
| `SetLoggingLevel(level)` | `Off/Error/Warning/Info/Debug` (default: `Info`). |
| `SetServiceMutator(mutator)` | Replace the callback handler (initially an `ApproovDefaultMessageSigning` instance). **Every successful `Initialize` call discards a custom mutator and restores the default, including a same-config re-initialization; reinstall it after initializing.** Pass `null` to restore a newly configured default signing mutator. Install `ApproovServiceMutatorDefault.Shared` explicitly to disable signing. Both provided mutator classes expose virtual callbacks for selective customization. |

## Substitution

| Method | Description |
|--------|-------------|
| `AddSubstitutionHeader(header, requiredPrefix?)` | Substitute header value with Approov secure string. |
| `RemoveSubstitutionHeader(header)` | Remove substitution. |
| `AddSubstitutionQueryParam(key)` | Substitute query param value with secure string. |
| `RemoveSubstitutionQueryParam(key)` | Remove substitution. |
| `AddExclusionURLRegex(pattern)` | Exclude matching URLs from Approov request mutation. The pattern is also the removal key. |
| `AddExclusionURLRegex(name, pattern)` | Compatibility overload that registers the pattern under an explicit removal key. |
| `RemoveExclusionURLRegex(nameOrPattern)` | Remove an exclusion using its explicit name or single-argument pattern. |

## SDK operations

| Method | Description |
|--------|-------------|
| `Precheck()` | Perform an attestation precheck through a dummy secure-string lookup; `UNKNOWN_KEY` is a successful precheck result. Bypass mode fails with `PermanentException`. |
| `FetchApproovToken(url)` | Fetch a token for a URL. Tokens are short-lived request artifacts and must never be cached by the application. |
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
| `ApproovMessageHandler` | `DelegatingHandler` calling `UpdateRequestWithApproov`; redirects re-enter processing, stale security headers are removed, and cross-origin credentials—including configured substitution query parameters—are stripped conservatively. Known custom terminal handlers have redirects disabled automatically; other types require the explicit `automaticRedirectsAlreadyDisabled: true` constructor after caller-side configuration. |
| `ApproovService.VerifyServerTrust(request, cert, chain, errors)` | Android/custom-handler TLS callback that preserves platform validation and then checks Approov pins across the validated chain. |
| `ApproovService.VerifyPinning(request, chainCertificates)` | Applies Approov pinning to an `IReadOnlyList<X509Certificate2>` certificate chain. |
| `ApproovService.UpdateRequestWithApproov(request)` | Core request mutation; returns `ApproovUpdateResponse`. |

## Message signing

`ApproovDefaultMessageSigning` is registered automatically as the initial mutator and adds RFC 9421
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

- **Fail-open** — if the SDK cannot provide a signature, throws while signing, returns malformed
  Base64/DER, or signature serialization fails, the error is logged and the request proceeds with
  no `Signature`/`Signature-Input` headers. No configured factory also leaves the request unsigned.
- **Fail-closed** — an unsupported signing algorithm or failure to create a required
  `Content-Digest` propagates and aborts the request.
