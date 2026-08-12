# Changelog

## [Unreleased]

### Documentation
- **Added a badge row, a guarded initialization example, and an obsolete-API section.** The README
  now carries .NET/MAUI/platform/message-signing badges and links to `CHANGELOG.md`. `USAGE.md`
  gained an `Initialization` section showing a `try`/`catch` startup that confirms
  `IsApproovEnabled()`, records `GetDeviceID()` with an app-generated session id, and falls back to
  bypass (`Initialize("")`) on failure. `REFERENCE.md` documents `Prefetch()`,
  `SetProceedOnNetworkFail(proceed)`, and `SetApproovInterceptorExtensions(callbacks)` as
  obsolete and intentionally not implemented, with their replacements.
- **Documented the empty token/trace contract (no behavioral change).** Empty token and trace
  headers are omitted (never sent empty-valued or prefix-only), matching the reference React Native
  layer; `SetUseApproovStatusIfNoToken` is the mechanism for surfacing the fetch status to the
  backend when no token is available. The canonical root requirement was updated to require omission,
  resolving a prior repository inconsistency. Behavior is already covered by
  `UpdateRequest_TraceIDNullOrEmpty_DoesNotAddTraceHeader`.
- **Clarified `SetLoggingLevel` scope (no behavioral change).** Documented that it controls only
  this service layer's own logging — emitted through a release-safe platform sink — and that the
  native Approov SDK exposes no log-level control to the layer, matching the logging contract of
  the other Approov service layers (e.g. React Native).

### Security
- **Test-only members no longer ship.** `ApproovService.ResetForTesting` and
  `SignatureParametersFactory.NowSeconds` were compiled into the release assembly and the
  NuGet package. `ResetForTesting` cleared the initialized flag, and both TLS entry points
  return true while uninitialized, so one reflective call disabled Approov certificate
  pinning and token injection process-wide. Both are now behind an `APPROOV_TESTING` constant
  defined only by the test project.
- **Synchronous `HttpClient.Send` no longer bypasses Approov.** Only `SendAsync` was
  overridden, so the inherited `DelegatingHandler.Send` forwarded straight to the transport
  with no token, no message signature and no secure string substitution. `Send` now throws
  `NotSupportedException` naming the asynchronous API.
- **Four TLS pinning bypasses closed.** A custom service mutator could switch pinning off on
  Android; an empty host was accepted; an internationalized host missed the punycode-keyed
  pin lookup and fell through to accept; and the constructors taking a caller-supplied
  handler installed no pinning callback at all.
- **Logging survives release builds.** The only sink was `Debug.WriteLine`, which is
  `[Conditional("DEBUG")]`, so every Approov log was erased from the shipped package and
  `SetLoggingLevel` had no effect. Logging now goes to logcat on Android and the device
  console on iOS, which restores visibility of every fail-open path.

### Changed
- **Every successful initialization now resets runtime configuration and any custom service
  mutator**, including a same-config re-initialization. Previously this state was preserved.
  A discarded custom mutator or binding header is logged at warning level. This matches the
  React Native service layer; see MIGRATION.md.

## [3.5.5] - 2026-07-08

### Added
- Unified .NET MAUI Approov service layer with full feature parity with `approov-service-urlsession`.
- HTTP message signing mirroring `approov-service-retrofit`: `ApproovDefaultMessageSigning` is registered as the service mutator (`SetServiceMutator`) and configured by a fluent `SignatureParametersFactory` with a ready-made default from `GenerateDefaultSignatureParametersFactory()`. Supports installation signing (`ecdsa-p256-sha256`, signature id `install`, the default) and account signing (`hmac-sha256`, signature id `account`); only requests carrying an Approov token are signed. Installation ES256 signatures are emitted as the raw R‖S (64-byte) form required by RFC 9421 §3.3.4 (the SDK's base64 ASN.1 DER is decoded).
- `@target-uri` derived component (RFC 9421 §2.2.2) in the component provider.
- `SetApproovTraceIDHeader` — attach the Approov trace ID to requests.
- `GetInstallMessageSignature` / `GetAccountMessageSignature` — native message-signature access on Android and iOS.
- `SetDevKey` — developer key injection.
- Configurable failure cache TTL (`SetFailureCacheTTL`, default 5 s) with failure-cache concurrency coalescing.
- RFC 8941 §4.1.8 / RFC 9421 §2.5 serialization compliance tests.

### Changed
- Single multi-targeted package (`net9.0-android;net9.0-ios`), replacing the per-platform projects.
- `IApproovTokenFetchResult` now includes a `TraceID` property.
- Default request behavior, status handling, token binding, secure-string substitution, dynamic configuration updates, message signing, and trace headers now follow the React Native service layer.
- Redirects are processed explicitly so every target is retokenized/resigned and cross-origin credentials are stripped.
- iOS TLS validation now evaluates the original native `SecTrust`; Android pinning checks the callback's complete peer chain, including `ExtraStore` intermediates.
- `ApproovServiceMutatorDefault` and `ApproovDefaultMessageSigning` callbacks are virtual for selective custom policy overrides.
- Operational message-signing failures now consistently fail open and remove stale signature headers; unsupported algorithms and required body-digest failures remain fail closed.
- `SetServiceMutator(null)` now restores the automatic default signing mutator. Install `ApproovServiceMutatorDefault.Shared` explicitly to disable signing.
- Added the common single-argument `AddExclusionURLRegex(pattern)` API while retaining the named overload for compatibility.

### Removed
- Inheritance-based `ApproovService` (replaced with a static `partial class`).
