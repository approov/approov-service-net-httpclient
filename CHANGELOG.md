# Changelog

## [Unreleased]

### Changed
- HTTP message signing reworked to mirror `approov-service-retrofit`. `ApproovDefaultMessageSigning` is now the service mutator (register via `SetServiceMutator`), configured by a fluent `SignatureParametersFactory` with a ready-made default from `GenerateDefaultSignatureParametersFactory()`. Supports installation signing (`ecdsa-p256-sha256`, signature id `install`, default) and account signing (`hmac-sha256`, signature id `account`); only requests carrying an Approov token are signed.
- ES256 signatures are emitted as the raw R‖S (64-byte) form required by RFC 9421 §3.3.4 (the SDK's base64 ASN.1 DER is decoded).
- Android bindings for `GetInstallMessageSignature` / `GetAccountMessageSignature` now call the native SDK directly (install signing is available on Android).

### Added
- `@target-uri` derived component (RFC 9421 §2.2.2) in the component provider.
- RFC 8941 §4.1.8 / RFC 9421 §2.5 serialization compliance tests.

### Removed
- `IApproovMessageSigner` / `IApproovAccountMessageSigner` (bring-your-own-key signer interfaces) and the standalone `ApproovService.SignRequest` step — superseded by the factory-based `ApproovDefaultMessageSigning` mutator.

## [3.5.11] - 2026-07-01

### Added
- Full feature parity with `approov-service-urlsession` v3.5.11
- HTTP message signing support (`IApproovMessageSigner` + `ApproovDefaultMessageSigning`)
- `SetApproovTraceIDHeader` — attach Approov trace ID to requests
- `GetInstallMessageSignature` on iOS; graceful `null` return on Android
- `SetDevKey` — developer key injection
- Configurable failure cache TTL (`SetFailureCacheTTL`, default 5 s)
- Failure cache concurrency coalescing (`ManualResetEventSlim`)

### Changed
- Project restructured as single multi-targeted package (`net8.0-android;net8.0-ios`)
- User-property string updated to `"approov-service-maui/3.5.11"`
- `IApproovTokenFetchResult` now includes `TraceID` property

### Removed
- Inheritance-based `ApproovService` (replaced with static `partial class`)
