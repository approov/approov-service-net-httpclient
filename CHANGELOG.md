# Changelog

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
