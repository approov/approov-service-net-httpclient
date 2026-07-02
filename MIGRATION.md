# Migrating from approov-service-xamarin

## Package reference

Replace:
```xml
<PackageReference Include="Approov.HttpClient.Xamarin" />
```
With:
```xml
<PackageReference Include="Approov.Service.Maui" Version="3.5.11" />
```

## Namespace

The namespace remains `Approov`. No namespace change required.

## Breaking changes

| Old | New |
|-----|-----|
| `ApproovService.Initialize(config)` | `ApproovService.Initialize(config, comment?)` — same signature, `comment` is optional |
| `new ApproovHttpClient()` | `new ApproovHttpClient()` — identical |
| User-property string `"approov-service-xamarin"` | Now `"approov-service-maui/3.5.11"` — set automatically |

## New features in 3.5.11

- `SetApproovTraceIDHeader` — propagate the Approov trace ID to a request header
- `GetInstallMessageSignature` — per-install message signing (iOS only; returns `null` on Android)
- `SetDevKey` — developer key injection
- HTTP message signing via `IApproovMessageSigner` (opt-in, fail-open)
- Configurable failure cache TTL (`SetFailureCacheTTL`)
