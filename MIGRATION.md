# Migrating from approov-service-xamarin

## Package reference

Replace:
```xml
<PackageReference Include="Approov.HttpClient.Xamarin" />
```
With:
```xml
<PackageReference Include="Approov.Service.Maui" Version="3.5.5" />
```

## Namespace

The namespace remains `Approov`. No namespace change required.

## Initialization resets runtime configuration

Every successful `ApproovService.Initialize` call resets the token header, token prefix,
trace header, binding header, status-if-no-token flag, body digest settings, substitution
headers, substitution query parameters, exclusion regexes, and any custom service mutator.
This applies to a re-initialization with the **same** configuration, and to one where the
platform SDK reports it was already initialized.

Apply configuration after initializing:

```csharp
ApproovService.Initialize(config);
ApproovService.AddSubstitutionHeader("X-Api-Key", null);
ApproovService.SetServiceMutator(new MyMutator());
```

If your app initializes more than once in a process (a hot restart, a tenant switch, a
re-login flow), reapply the configuration after each call. A discarded custom mutator or
binding header is logged at warning level.

This matches the React Native service layer. Earlier MAUI builds preserved this state across
a same-config re-initialization, so an app relying on that will now see default behaviour
until it reapplies its configuration.

## Breaking changes

| Old | New |
|-----|-----|
| `ApproovService.Initialize(config)` | `ApproovService.Initialize(config, comment?)` — same signature, `comment` is optional |
| `new ApproovHttpClient()` | `new ApproovHttpClient()` — identical |
| User-property string `"approov-service-xamarin"` | Now `"approov-service-maui/3.5.5"` — set automatically |

## New features in 3.5.5

- `SetApproovTraceIDHeader` — propagate the Approov trace ID to a request header
- `GetInstallMessageSignature` — per-install message signing (Android and iOS; returns `null` in bypass mode)
- `SetDevKey` — developer key injection
- Automatic fail-open HTTP message signing via the initial `ApproovDefaultMessageSigning` mutator
- Configurable failure cache TTL (`SetFailureCacheTTL`)
