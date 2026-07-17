# Changelog

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

### Removed
- Inheritance-based `ApproovService` (replaced with a static `partial class`).
