# Changelog

## [Unreleased]

### Fixed
- **A caller's own TLS callback can no longer replace Approov pinning.** The constructors taking a
  caller-supplied handler installed pinning only when the certificate-callback slot was free, so a
  permissive callback of the caller's own (including `(_, _, _, _) => true`) left tokenized and
  signed requests running over an unpinned connection, with no error and no log. Approov pinning is
  now **composed in front of** any existing callback on `HttpClientHandler`,
  `SocketsHttpHandler`, `AndroidMessageHandler` and `NSUrlSessionHandler`: it runs first and
  short-circuits, so a pin mismatch rejects the connection without consulting the caller's
  callback, and the caller's callback can only further restrict what pinning accepted. Composition
  is logged at warning level. The hand-wired `VerifyServerTrust` pattern in `USAGE.md` keeps
  working, because that function is pure. Two unit tests that asserted the old
  "keep the caller's callback" behavior were replaced.
- **The `automaticRedirectsAlreadyDisabled: true` constructor no longer skips pinning.** That flag
  acknowledges the redirect requirement only, but the constructor passed the handler straight
  through, so a documented public path — including
  `new ApproovMessageHandler(new HttpClientHandler { AllowAutoRedirect = false }, true)` — produced
  tokenized, signed requests over an unpinned connection with no error and no log. Pinning
  installation is now a separate pass run by both constructors, applied to the terminal handler of
  a `DelegatingHandler` chain. A terminal type that exposes no certificate callback still cannot be
  pinned by the layer (the escape hatch is required for test doubles and custom transports), but it
  is now reported at **error** level naming the type, instead of failing silently.
- **Caller-supplied `AndroidMessageHandler` and `NSUrlSessionHandler` now get pinning at all.**
  Both are terminal handlers, and both previously had only `AllowAutoRedirect = false` applied,
  so a custom transport of either type ran completely unpinned while still carrying a token and
  signature. Pinning is now installed on both (`ServerCertificateCustomValidationCallback` and
  `TrustOverrideForUrl` respectively, the latter to keep evaluating the original native
  `SecTrust`).
- **Android: intermittent "Unknown approov token fetch result SUCCESS".** On .NET Android the SDK
  `TokenFetchStatus` binds as a `Java.Lang.Enum`, so its managed peer is marshalled across JNI on
  every access; reading the live fetch result repeatedly could observe the status inconsistently
  (unmatched on one read, yet "SUCCESS" when stringified) — the failure reported from production.
  The native fetch result is now snapshotted once at the fetch boundary (`SnapshotTokenFetchResult`),
  on the calling thread immediately after the synchronous fetch, so every consumer reads one stable
  value. This removes the repeated-read, reference-comparison and cross-thread-read paths behind the
  failure. Covered by `SnapshotTokenFetchResultTests`. On-device validation that it *eliminates*
  (rather than merely reduces) the intermittent case is still pending.

### Documentation
- **`HandlePinningShouldProcessRequest` is documented as not consulted.** `USAGE.md` listed
  "Customize pinning decisions per request" as a reason to use a mutator, but honouring that hook
  was one of the pinning bypasses closed in 3.5.5, so the hook has no effect here. The bullet is
  removed, the interface member carries an XML doc saying so, and `REFERENCE.md` gained a
  "Deliberate divergences from the other service layers" table recording that the okhttp,
  retrofit, HttpsUrlConnection and URLSession-family layers do honour it while this layer does
  not. The member itself is retained for source compatibility with cross-platform mutator code.
- **Corrected the required-body-digest contract.** `USAGE.md` said an *empty* body fails in
  required mode; the code fails only when the body is missing or its length is unknown. A
  zero-length body has a known length and yields the valid digest of an empty payload per
  RFC 9421, so the documentation now says missing, unknown-length (streamed/chunked) or
  non-replayable, and states the zero-length case explicitly.
- **Refreshed the pinning section of `USAGE.md`** to state that pinning is installed on
  caller-supplied handlers automatically and composed in front of any existing callback.
- **Removed the internal verification report** (`SERVICE_LAYER_VERIFICATION.md`). It recorded an
  internal test run, including internal test-service hostnames and an observed SPKI pin value, which
  do not belong in a customer-facing repository. Verification records are kept internally.
- **Noted the .NET 10 SDK build warning.** Building `net9.0-android` with the .NET 10 SDK still
  succeeds but emits `warning NETSDK1202: The workload 'net9.0-android' is out of support`. .NET 10
  support is tracked separately.
- **Added a badge row, a guarded initialization example, and an obsolete-API section.** The README
  now carries .NET/MAUI/platform/message-signing badges and links to `CHANGELOG.md`. `USAGE.md`
  gained an `Initialization` section showing a `try`/`catch` startup that confirms
  `IsApproovEnabled()`, records `GetDeviceID()` with an app-generated session id, and falls back to
  bypass (`Initialize("")`) on failure. `REFERENCE.md` documents `Prefetch()`,
  `SetProceedOnNetworkFail(proceed)`, and `SetApproovInterceptorExtensions(callbacks)` as
  obsolete and intentionally not implemented, with their replacements.
- **Confirmed the persistent token-binding contract (no behavioral change).** Approov binding is
  persistent native SDK state: once a `pay` claim is set in a running app it can be changed but not
  removed, so a request missing the configured binding header leaves the current binding unchanged.
  The layer already implements this and `USAGE.md`/`REFERENCE.md` already document it (including the
  no-mixing rule for manual/automatic binding); the canonical root requirement was updated to model
  persistent binding, resolving the prior "remove `pay`" expectation.
- **Corrected the same-config re-initialization documentation (no behavioral change).** `USAGE.md`
  and the `REFERENCE.md` `Initialize` row previously said a same-config re-initialization *preserves*
  runtime settings and the custom mutator. The code and tests already **reset** on every successful
  initialization (including a same-config one), matching React Native and the root requirements; the
  prose lagged and now matches. Re-apply `SetServiceMutator` and runtime configuration after each
  initialization.
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
