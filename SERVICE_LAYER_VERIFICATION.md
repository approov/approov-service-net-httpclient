# MAUI .NET HttpClient Service-Layer Verification

Initial verification: 17 July 2026; fixes retested 20 July 2026

Service-layer base commit: `ff5683932250c2863b09497e73f4093c61bc6e5e` (`feature/3.5.5`) plus the fixes described below
Canonical test repository: `approov/core-service-layers-testing` at `8fa5fd812b22153a81a178bcae5d44a0f779484d`

## Executive conclusion

The corrected service layer builds and packages successfully and passes its complete 259-test unit suite. Its principal request, TLS pinning, substitution, secure-string, JWT, mutator, and HTTP message-signing paths work in simulator testing. Production-SDK testing successfully reached the protected v3 token-binding endpoint on Android and iOS, and the v5 installation-message-signing endpoint on Android.

The 20 July fixes resolved the actionable implementation findings:

1. Operational signing failures now fail open, remove stale signature headers, and log at error level; required-digest and unsupported-algorithm failures remain fail closed.
2. `SetServiceMutator(null)` restores a new default signing mutator.
3. The common single-argument exclusion-regex API is available while retaining the named compatibility overload.
4. Documentation now explains persistent binding, manual binding isolation, token non-caching, null-mutator behavior, and the signing failure contract.

Full canonical conformance depends on resolving two specification conflicts. Same-config initialization now resets runtime configuration and the custom mutator, matching React Native and the root requirements; empty token/trace artifacts are omitted to match React Native, while the root requirements demand the opposite. The root missing-binding requirement is also incompatible with the production SDK contract: official Approov documentation states that once `pay` is enabled in a running app it can be changed but not removed. Native SDK logging-level forwarding remains unproven.

## Scope and method

The verification mapped the MAUI layer to the root and service-layer-content requirements in `core-service-layers-testing`, then exercised the implementation at four levels:

- all repository unit tests;
- Release builds and NuGet package creation;
- restore/build from those packages in a clean MAUI consumer;
- Android and iOS simulator execution using the canonical mini-SDK/test-support behavior and the canonical replay workers.

The simulator sequencer ran 40 checks per platform. It covered lifecycle, bypass, protected and unprotected requests, token binding, custom headers, trace handling, substitutions, exclusions, precheck, secure strings, custom JWTs, mutators, signing, body digests, and real TLS pinning. Temporary bindings and the test sequencer were created outside this repository; no mini-SDK or test-only code was added to the production package.

The following canonical replay services were used:

- `https://replay.ivol.workers.dev`
- `https://replay-unprotected.ivol.workers.dev`
- `https://pinning-only.ivol.workers.dev`

The live SPKI pin observed during this run was `cs+6+FJ1NNBVMPf4Nx32VDbYpPWc884KR6NJWqrSlA8=`. Each replay service was reachable with HTTP 200 from the host at test time.

## Build, unit, and package verification

| Verification | Result | Evidence |
|---|---:|---|
| Complete unit suite | PASS | 259 passed, 0 failed |
| Android Release service build | PASS | 0 warnings, 0 errors |
| iOS simulator Release service/binding build | PASS | 0 warnings, 0 errors |
| Service NuGet package | PASS | `Approov.Service.Maui.3.5.5.nupkg`, ZIP integrity clean |
| iOS binding NuGet package | PASS | `Approov.iOS.Binding.3.5.5.nupkg`, ZIP integrity clean |
| Clean package-consumer Android build | PASS | Local packages restored; 0 warnings, 0 errors |
| Clean package-consumer iOS build | PASS | Local packages restored; 0 warnings, 0 errors |

The package-consumer check is important because it verifies package contents and transitive references rather than merely building against repository project references.

## Device-flow results

### Raw simulator result

| Platform | Passed | Failed | Notes |
|---|---:|---:|---|
| Android emulator | 28 | 12 | Seven signing assertions were affected by the substituted Android mini-SDK installation-key limitation; the other failures are specification/test-support issues and an invalid harness setup. |
| iOS simulator | 35 | 5 | Remaining failures are three specification conflicts, the mini-SDK empty-value inconsistency, and an invalid required-one-shot harness setup. |

Raw counts alone are misleading because the same Android mini-SDK installation-key limitation caused several dependent signing assertions to fail. The adjudicated results below separate product failures from harness and test-support behavior.

### Canonical flow matrix

| Area | Android | iOS | Adjudicated result |
|---|---:|---:|---|
| Initial state, bypass, and bypass-to-active upgrade | PASS | PASS | PASS |
| Bypass still enforces operating-system trust | PASS | PASS | PASS |
| Protected and unprotected request handling | PASS | PASS | PASS |
| Token header, prefix, trace header, and null-prefix customization | PASS | PASS | PASS |
| Trace disable while token/signing continue | Mini-SDK limitation | PASS | PASS with iOS device and unit evidence |
| Present-header token binding | PASS | PASS | PASS |
| Missing binding after a prior bound request | FAIL | FAIL | **SPECIFICATION CONFLICT:** production SDK binding is persistent and cannot be removed in a running app |
| Empty binding value | TEST SUPPORT MISMATCH | TEST SUPPORT MISMATCH | Service unit test proves the empty string is forwarded; current mini-SDK omits `pay` for blank data despite the root requirement expecting SHA-256 of empty input |
| Empty token and trace artifacts | FAIL | FAIL | **CONFIRMED FAILURE:** empty trace is omitted; static code also omits an empty token |
| Header and query substitution, including removal | PASS | PASS | PASS |
| Exclusion add/remove behavior | PASS | PASS | PASS |
| Precheck | PASS | PASS | PASS |
| Secure string lookup, define, delete, empty, null, unknown | PASS | PASS | PASS |
| Custom JWT: normal, 18 KB, malformed, network error, rejected | PASS | PASS | PASS |
| Default fail-closed mutator and custom status-as-token mutator | Signing-dependent limitation | PASS | PASS with iOS device and unit evidence |
| Installation-key HTTP message signing | PASS on real SDK; mini-SDK limitation | PASS with mini-SDK | Service pipeline verified; real-SDK iOS physical-device enforcement still pending |
| Account-key HTTP message signing | Unit evidence; mini-SDK limitation | PASS | PASS for implementation, with Android production endpoint coverage pending |
| RFC 8941 byte sequence and raw 64-byte P-256 signature | Mini-SDK limitation | PASS | PASS with iOS device and unit evidence |
| POST/PUT/PATCH `Content-Digest` and signature coverage | Mini-SDK limitation | PASS | PASS with iOS device and unit evidence |
| Optional one-shot body proceeds signed without digest | Mini-SDK limitation | PASS | PASS with iOS device and unit evidence |
| Required one-shot body fails closed | HARNESS INVALID | HARNESS INVALID | PASS in production unit suite; the device sequencer had retained a custom signer, so it did not configure the factory required by this test |
| Unavailable installation key proceeds unsigned | PASS | PASS | PASS |
| Valid, invalid, accept-any, unprotected, and excluded-host TLS pins | PASS | PASS | PASS |
| Forced pin refresh produces immediate retryable failure | PASS | PASS | PASS |
| Same-config successful reinitialization resets runtime state | PASS | PASS | Resets runtime configuration and the custom mutator after every successful initialization, matching React Native |

## v3 and v5 endpoint evidence

Production-SDK Shapes tests were repeated from the corrected final tree on 20 July 2026:

| Endpoint | Platform/evidence | Result |
|---|---|---:|
| v3 protected endpoint | Android production SDK | HTTP 200; binding `pay` verified against the supplied header value |
| v5 installation-signed request, missing trailing slash | Android production SDK | HTTP 400 `malformed message signature`; diagnostic harness used `/v5/shapes`, which does not match the documented signed target URI |
| v5 installation-signed request, documented URI | Android production SDK | HTTP 200 using `/v5/shapes/`; backend reported a valid message signature |
| v3 protected endpoint | iOS production SDK simulator | HTTP 200; binding hash verified |
| v5 endpoint reachability | iOS production SDK simulator | HTTP 200 with no TLS/transport failure; simulator SDK did not expose an installation key/signature |

The initial v5 diagnostic incorrectly hard-coded `https://shapes.approov.io/v5/shapes` without the trailing slash used by the quickstart and its documentation. The message signature covers `@target-uri`, so `/v5/shapes` and `/v5/shapes/` produce different signature bases. The Shapes routing/verifier path normalizes or expects the documented slash form, causing the no-slash request to be rejected. A freshly signed request to `https://shapes.approov.io/v5/shapes/` returned HTTP 200. The production signing code did not change between these checks; its canonical builder, component provider, and SFV serializer remain byte-identical to the base commit.

The iOS mini-SDK run additionally proves the MAUI service-layer signing pipeline, signature serialization, digest coverage, and account/installation-key selection on a simulator. It does not replace a real-SDK, physical-iOS-device v5 enforcement test. The available physical iOS devices were offline during this run.

## Confirmed implementation/specification gaps

### 1. Same-configuration reinitialization — RESOLVED

`ApproovService.Initialize` now calls `ResetRuntimeConfiguration` after every successful
initialization, including a same-config one and one where the platform SDK reports it was
already initialized. The reset also replaces any custom service mutator with a fresh default,
and logs a warning when it discards a custom mutator or a configured binding header.

This section previously described the preservation behaviour as a cross-repository contract
decision matching React Native. That justification was incorrect. React Native resets both
runtime configuration and the custom mutator on every initialize, on Android
(`ApproovService.java`) and iOS (`ApproovService.m`), covers it with the named regression
tests `initializeWithSameConfigResetsRuntimeConfiguration` and
`initializeWithSameConfigResetsCustomServiceMutator`, and records the mutator reset in its
changelog as security relevant. MAUI was doing the opposite of the reference it cited.

The behaviour now matches the reference and the common requirements. This is a behavioural
change for existing MAUI integrations: an application that configures headers, substitutions,
exclusions or a custom mutator before a later `Initialize` call must reapply that
configuration afterwards. See MIGRATION.md.

### 2. Persistent token binding — canonical requirement is not implementable

When the configured binding header is present, its serialized value is sent to the SDK before fetching the token. The canonical test expects a subsequent missing header to remove `pay`. However, the production SDK declares the value non-null and the official token-binding documentation states that once `pay` has been added it cannot be removed in the running app, only changed: <https://approov.io/docs/latest/approov-usage-documentation/#token-binding>.

An experimental nullable call cleared the mini-SDK state on Android. On iOS, the generated binding correctly rejected null because the native selector is non-null. Overriding that metadata made the mini-SDK test pass but would violate the supported production SDK contract, so that unsafe change was not retained.

Required resolution: update the canonical requirement and harness to model persistent binding, matching the production SDK and React Native behavior. Applications should bind to a header that remains present for the protected session. If removal during an app process is a product requirement, the native SDK must first provide and document a supported clear API.

### 3. Empty token/trace artifacts

The service currently adds the token and trace headers only when their values are non-empty. The root common requirement says that empty artifacts must be emitted to demonstrate that Approov processing occurred. Both device runs observed the missing empty trace header; the equivalent token condition is visible in the request mutation code.

Required resolution: choose one canonical contract. If empty headers are required, remove the non-empty guards while retaining null handling. If omission is intended, update the root requirement; the React Native coverage audit already describes omission as the expected behavior, so the canonical repository is internally inconsistent here.

### 4. Signing failure policy — fixed 20 July 2026

The signer now catches operational SDK, Base64, DER, and serialization failures; removes `Signature` and `Signature-Input`; logs the failure at error level without logging signing material; and proceeds unsigned. Required body-digest and unsupported-algorithm failures are explicitly rethrown.

Regression tests cover SDK exceptions, malformed Base64, malformed DER, stale-header removal, and required-digest fail-closed behavior.

### 5. Common API alignment

- `SetServiceMutator(null)` now restores a newly configured `ApproovDefaultMessageSigning` instance. Consumers can explicitly install `ApproovServiceMutatorDefault.Shared` to disable signing.
- `SetLoggingLevel` controls this layer's own logging, filtered by level and emitted through a
  release-safe platform sink (`android.util.Log` / `NSLog`). **Logging scope resolved:** the native
  Approov SDK exposes no log-level API on either platform (confirmed against the Android and iOS SDK
  headers), and no other service layer forwards a level to the SDK — React Native's `setLogLevel`
  likewise gates only the wrapper's logging. `SetLoggingLevel` is therefore service-layer-scoped by
  design; this is now documented in `USAGE.md`/`REFERENCE.md`. Level filtering is covered by
  `ApproovServiceLoggingTests` (`Off` suppresses all; `Error` suppresses info but keeps errors).
- `AddExclusionURLRegex(pattern)` now provides the common form; the named overload remains for compatibility.

Null-mutator, exclusion-regex, and logging-level behavior now match the common interface.

## Canonical test-repository inconsistencies found

The following issues in `core-service-layers-testing` affected interpretation of the results:

1. The missing-binding rule requires removal of `pay`, but the production SDK documents binding as non-removable during the running app.
2. The root requirement expects an empty binding value to produce SHA-256 of the empty string, but the tested mini-SDK path omits `pay` for blank data.
3. The root requirement requires empty token/trace headers, while the React Native coverage audit treats their omission as passing behavior.
4. The mini-SDK README identifies `approov.io` as the default protected domain, while the compiled Android mini-attester configuration protects `replay.ivol.workers.dev`.
5. The same-config reset rule conflicts with the React-Native-aligned preservation behavior currently documented and tested in this MAUI layer.

These should be corrected or versioned in the canonical repository before the same harness is used as a release gate across every language implementation.

## Documentation audit

The functional documentation is substantial. The following content-requirement gaps were **addressed**:

- the required badge row was added and the README now links directly to usage, reference, changelog, and migration material (**done**);
- `USAGE.md` gained an `Initialization` section with a complete guarded example — state confirmation (`IsApproovEnabled`), device ID (`GetDeviceID`), an app-generated session/correlation id, and bypass fallback (`Initialize("")`) (**done**);
- `REFERENCE.md` now has an obsolete/deprecated-API section documenting `Prefetch`, `SetProceedOnNetworkFail`, and `SetApproovInterceptorExtensions` as intentionally not implemented, with replacements (**done**).

Remaining:

- same-config reinitialization documentation in `USAGE.md` still describes the React-Native-aligned preservation behavior, which conflicts with the code (which now resets) and the root common requirements. This is tracked with the same-config canonical decision and is not resolved by this documentation pass.

## Remaining verification limits

- A connected physical iOS device with the production SDK is still required to prove real installation-key v5 enforcement end to end.
- The v3 backend negative control for a valid token with an intentionally wrong binding hash was not available.
- Android production endpoint account-key signing was not exercised; it is covered by unit tests and the iOS mini-SDK device flow.
- Redirect behavior, request concurrency, and failure-cache coalescing are covered by the 259-test unit suite but were not repeated over the simulator network harness.
- The substitution matrix did not separately exercise every empty/single-character/long value combination, although normal secure strings and an 18 KB custom JWT were tested.
- No live Approov administration changes were made; deterministic statuses and pins came from the canonical mini-SDK.
- Cross-service-layer initialization in the same process is not meaningful in this standalone MAUI consumer and was not exercised.
- The package was created and consumed locally; publication to NuGet was not part of this verification.

## Release recommendation

The signing-policy and common-API implementation findings are resolved, and Android production-SDK requests now pass both the v3 and v5 Shapes endpoints when the documented URLs are used. Before claiming full common-service-layer conformance, update the canonical repository to reflect supported persistent binding and make explicit decisions for same-config reset and missing artifacts. Also verify native logging semantics and complete v5 enforcement on a connected physical iOS device before release publication.
