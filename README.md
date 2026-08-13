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
- `iOS.Binding` — Objective-C binding project over the Approov iOS SDK (`Approov.xcframework`)
- `libs/` — where you place the Approov Android SDK (`approov.aar`); see [libs/README.md](libs/README.md)
- `iOS.Binding/libs/` — where you place `Approov.xcframework`; see [iOS.Binding/libs/README.md](iOS.Binding/libs/README.md)
- `ApproovService.MAUI.Tests` — xUnit test suite over the shared (platform-independent) code

## Installation

### 1. Provide the Approov SDK binaries

This repository contains only the wrapper source (MIT). The Approov SDK itself is proprietary
and is **not** committed here, so you fetch it from your own Approov account with the
[`approov` CLI](https://approov.io/docs/latest/approov-installation/) before building:

```bash
# from the repository root
approov sdk -getLibrary libs/approov.aar                              # Android
approov sdk -getLibrary iOS.Binding/libs/Approov.xcframework          # iOS
```

This service layer was tested against Approov SDK **3.5.3**. Each destination directory has a
README with the exact expectations, verification commands and version caveats:
[libs/README.md](libs/README.md) (Android) and
[iOS.Binding/libs/README.md](iOS.Binding/libs/README.md) (iOS — read the Objective Sharpie
version-drift warning if you use a version other than 3.5.3).

### 2. Reference the service layer

Until the `Approov.Service.Maui` NuGet package is published, reference the project directly
from your app's `.csproj`:

```xml
<ProjectReference Include="path\to\approov-service-net-httpclient\ApproovService.MAUI\ApproovService.MAUI.csproj" />
```

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
