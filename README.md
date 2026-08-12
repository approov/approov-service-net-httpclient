# Approov SDK bindings for .NET HttpClient

![.NET](https://img.shields.io/badge/.NET-9.0-512BD4?logo=dotnet&logoColor=white)
![.NET MAUI](https://img.shields.io/badge/.NET%20MAUI-net9.0--ios%20%7C%20net9.0--android-512BD4?logo=dotnet&logoColor=white)
![Platforms](https://img.shields.io/badge/Platforms-iOS%20%7C%20Android-3DDC84)
![Message Signing](https://img.shields.io/badge/Message%20Signing-RFC%209421-1f6feb)

A wrapper for the [Approov SDK](https://approov.io) to enable easy integration when using [`MAUI`](https://dotnet.microsoft.com/en-us/apps/maui) applications for making the API calls that you wish to protect with Approov using [`HttpClient`](https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/http/httpclient). In order to use this you will need a trial or paid [Approov](https://www.approov.io) account.

## Quick start

```csharp
// App startup (e.g. MauiProgram.cs)
ApproovService.Initialize("<your-config-string>");

// Use ApproovHttpClient instead of HttpClient
var client = new ApproovHttpClient();
var response = await client.GetAsync("https://your-api.example.com/endpoint");
```

Please see the [MAUI HttpClient](https://github.com/approov/quickstart-maui-httpclient) and
[MAUI Refit](https://github.com/approov/quickstart-maui-refit) quickstarts for worked examples.

## Features

- Automatic Approov token injection on protected requests
- HTTP header and query parameter secret substitution
- Platform-native TLS validation and full-chain public-key pinning
- Configurable failure caching (default 5 s) for network-outage resilience
- RFC 9421 installation message signing by default, with optional account signing
- Redirect retokenization/resigning with cross-origin credential stripping
- Bypass mode (`Initialize("")`) for development environments

## Structure

- `ApproovService.MAUI` — the service layer, multi-targeting `net9.0-android` and `net9.0-ios`
- `iOS.Binding` — Objective-C binding project for the bundled `Approov.xcframework`
- `libs/approov.aar` — the bundled Approov Android SDK
- `ApproovService.MAUI.Tests` — xUnit test suite over the shared (platform-independent) code

## Installation

Until the `Approov.Service.Maui` NuGet package is published, reference the project directly
from your app's `.csproj`:

```xml
<ProjectReference Include="path\to\approov-service-net-httpclient\ApproovService.MAUI\ApproovService.MAUI.csproj" />
```

The native Approov SDKs for both platforms are included in this repository, so no additional
setup is required.

## API Reference

See [REFERENCE.md](REFERENCE.md) for the full API surface.

## Usage

See [USAGE.md](USAGE.md) for detailed feature documentation and customization examples,
including a guarded initialization example (state confirmation, device ID, app session
correlation, and bypass fallback).

## Changelog

See [CHANGELOG.md](CHANGELOG.md) for the release history and behavioral changes.

## Migration from approov-service-xamarin

See [MIGRATION.md](MIGRATION.md).
