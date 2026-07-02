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
| `SetBodyDigestEnabled(bool enabled)` | Enable or disable automatic `Content-Digest` header generation for POST, PUT, and PATCH requests. Enabled by default. When enabled, the digest is computed and added before signing so signers can include `content-digest` as a signature component. Gracefully skips non-repeatable (streaming) bodies. |
| `SetFailureCacheTTL(seconds)` | Failure cache TTL (default: 5.0 s). |
| `SetLoggingLevel(level)` | `Off/Error/Warning/Info/Debug` (default: `Info`). |
| `SetServiceMutator(mutator)` | Replace callback handler (default: `ApproovServiceMutatorDefault.Shared`). |

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
| `FetchCustomJWT(payload)` | Fetch a custom JWT. |
| `GetDeviceID()` | Returns the Approov device ID (`null` in bypass mode). |
| `SetDataHashInToken(data)` | Hash arbitrary data into the token. |
| `SetDevKey(devKey)` | Set a developer key for testing. |
| `FetchConfig()` | Retrieve the latest SDK config string. |

## HTTP integration

| Type | Description |
|------|-------------|
| `ApproovHttpClient` | `HttpClient` subclass wired to `ApproovMessageHandler`. |
| `ApproovMessageHandler` | `DelegatingHandler` calling `UpdateRequestWithApproov`. |
| `ApproovService.VerifyPinning(request, cert)` | TLS pinning callback for `ServerCertificateCustomValidationCallback`. |
| `ApproovService.UpdateRequestWithApproov(request)` | Core request mutation; returns `ApproovUpdateResponse`. |
