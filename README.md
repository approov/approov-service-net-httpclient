# Approov Service for .NET MAUI HttpClient

![.NET](https://img.shields.io/badge/.NET-9.0-512BD4?logo=dotnet&logoColor=white)
![.NET MAUI](https://img.shields.io/badge/.NET%20MAUI-net9.0--android%20%7C%20net9.0--ios-512BD4?logo=dotnet&logoColor=white)
![Platforms](https://img.shields.io/badge/Platforms-Android%20minSdk%2023%20%7C%20iOS%2015%2B-3DDC84?logo=android&logoColor=white)
![Approov SDK](https://img.shields.io/badge/Approov%20SDK-3.5.3-1f6feb)
![Message Signing](https://img.shields.io/badge/Message%20Signing-RFC%209421-1f6feb)
![NuGet](https://img.shields.io/badge/NuGet-not%20yet%20published-lightgrey?logo=nuget&logoColor=white)
![License](https://img.shields.io/badge/License-MIT-green)

A wrapper for the [Approov SDK](https://approov.io) to enable easy integration when using [`.NET MAUI`](https://dotnet.microsoft.com/en-us/apps/maui) applications for making the API calls that you wish to protect with Approov using [`HttpClient`](https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/http/httpclient). In order to use this you will need a trial or paid [Approov](https://www.approov.io) account.

## ADDING APPROOV SERVICE DEPENDENCY

There is **no NuGet package yet** — one is planned — so the service layer is consumed as a **project reference**. Clone this repository alongside your app and add it to your app's `.csproj`:

```xml
<ItemGroup>
  <ProjectReference Include="..\approov-service-net-httpclient\ApproovService.MAUI\ApproovService.MAUI.csproj" />
</ItemGroup>
```

The service layer multi-targets `net9.0-android` and `net9.0-ios`, so your app must target the same (or a compatible) framework. On iOS the reference pulls in `iOS.Binding`, the Objective-C binding project over the Approov iOS SDK, automatically.

This repository is an open source wrapper layer (MIT) that allows you to easily use Approov with `HttpClient`. It has a further dependency on the closed source Approov SDK, which is **not** distributed here — see the next section.

## OBTAINING THE PLATFORM SDK BINARIES

The Approov SDK is proprietary and is not committed to this repository, so you fetch it from your own Approov account with the [`approov` CLI](https://approov.io/docs/latest/approov-installation/) before your first build. Run these from the repository root:

```bash
approov sdk -getLibrary libs/approov.aar                       # Android
approov sdk -getLibrary iOS.Binding/libs/Approov.xcframework   # iOS
```

The paths matter: the build looks for the artifacts in exactly these locations.

| Platform | Artifact | Location | Referenced by |
|---|---|---|---|
| Android | `approov.aar` | `libs/approov.aar` | `ApproovService.MAUI.csproj` — `<AndroidLibrary Include="../libs/approov.aar" />` |
| iOS | `Approov.xcframework` (a directory) | `iOS.Binding/libs/Approov.xcframework` | `iOS.Binding.csproj` — `<NativeReference Include="libs/Approov.xcframework">` |

This service layer was tested against Approov SDK **3.5.3**. Verify what you fetched:

```bash
unzip -p libs/approov.aar AndroidManifest.xml | grep versionName
plutil -p iOS.Binding/libs/Approov.xcframework/ios-arm64/Approov.framework/Info.plist | grep CFBundleShortVersionString
```

Each destination directory has a README with the full expectations, `approov sdk -list` / `-libraryID` usage for a specific version, and the caveats: [libs/README.md](libs/README.md) (Android) and [iOS.Binding/libs/README.md](iOS.Binding/libs/README.md). **Read the iOS one if you use a version other than 3.5.3** — the binding's `ApiDefinition.cs` is Objective Sharpie-generated against the 3.5.3 headers, and an added or changed API can bind incorrectly without any build error.

## MANIFEST CHANGES

The following app permissions need to be available in the Android manifest to use Approov:

```xml
<uses-permission android:name="android.permission.INTERNET" />
<uses-permission android:name="android.permission.ACCESS_NETWORK_STATE" />
```

**`ACCESS_NETWORK_STATE` is required, not optional.** Without it the SDK cannot query connectivity and every token fetch fails with an opaque `InternalError`, while initialization, `GetDeviceID()` and message signing all appear to work.

The minimum Android SDK version you can use with the Approov package is 23 (Android 6.0); the minimum iOS version is 15.0. No `Info.plist` changes are required on iOS.

Please [read this](https://approov.io/docs/latest/approov-usage-documentation/#targeting-android-11-and-above) section of the reference documentation if targeting Android 11 (API level 30) or above.

## INITIALIZING APPROOV SERVICE

In order to use `ApproovService` you must initialize it when your app starts, usually in `MauiProgram.cs`.

Wrap initialization in a try/catch: on success, confirm the layer is enabled and log the Approov device ID together with an app-generated session/correlation id; on failure, log it and continue **unprotected** by re-initializing with an empty config (bypass mode) so the app still functions.

```csharp
using Approov;

public static class ApproovStartup
{
    // App-generated id for correlating this app session with Approov metrics/logs.
    public static readonly string SessionId = Guid.NewGuid().ToString();

    public static void Initialize()
    {
        try
        {
            ApproovService.Initialize("<enter-your-config-string-here>");
            if (ApproovService.IsApproovEnabled())
                Console.WriteLine($"Approov enabled, deviceID={ApproovService.GetDeviceID()} session={SessionId}");
        }
        catch (Exception failure)
        {
            Console.WriteLine($"Approov initialization failed: {failure.Message}");
            ApproovService.Initialize("");   // bypass mode: the app keeps working, unprotected
        }
    }
}
```

The `<enter-your-config-string-here>` is a custom string that configures your Approov account access. This will have been provided in your Approov onboarding email. On success the example logs the Approov **device ID** (`GetDeviceID()`) and an **app-generated session/correlation id** so a given install can be correlated across your app logs, backend, and the Approov metrics. If initialization fails it re-initializes with an empty config so the app keeps working — but those requests go out **without Approov protection**, so the backend remains the enforcement point.

Note that **every** successful initialization is a boundary: it resets runtime configuration and discards a custom service mutator. Apply your configuration *after* initializing. See [USAGE.md](USAGE.md).

## USING APPROOV SERVICE

You can then make Approov enabled API calls by using `ApproovHttpClient` in place of `HttpClient`:

```csharp
using var client = new ApproovHttpClient();
using HttpResponseMessage response = await client.GetAsync("https://your-api.example.com/endpoint");
```

This client is wired to `ApproovMessageHandler`, which protects channel integrity (with either pinning or managed trust roots), and may also add `Approov-Token`, substitute app secret values, and add RFC 9421 message signatures, depending upon your integration choices. You should thus use this client for all API calls you may wish to protect.

Approov errors surface as an `ApproovException`, specialized into `NetworkingErrorException` (an issue with networking that should provide an option for a user initiated retry), `RejectionException` (the app or device failed attestation; carries the ARC and rejection reasons), `PermanentException`, `PinningErrorException`, `InitializationFailureException` and `ConfigurationFailureException`.

Use the **asynchronous** API. Synchronous `HttpClient.Send` throws `NotSupportedException` rather than reaching the network without a token, a signature or secret substitution.

## CUSTOM HTTP MESSAGE HANDLER

By default `ApproovHttpClient` builds the platform handler for you. If your existing code uses a customized handler — different timeouts, a cookie container, your own delegating handlers — pass it in:

```csharp
var inner = new HttpClientHandler { /* your configuration */ };
using var client = new HttpClient(new ApproovMessageHandler(inner));
```

The layer then, on the terminal handler of the chain: disables automatic redirects, so every redirect target re-enters the Approov pipeline and is retokenized and resigned; and installs Approov pinning, composing it **in front of** any certificate callback already present, so pinning runs first and short-circuits. A permissive callback of your own therefore cannot disable pin enforcement.

`HttpClientHandler`, `SocketsHttpHandler`, `AndroidMessageHandler` and `NSUrlSessionHandler` are all supported terminal types. For any other terminal handler type, disable its redirects yourself and acknowledge that explicitly:

```csharp
using var client = new HttpClient(
    new ApproovMessageHandler(inner, automaticRedirectsAlreadyDisabled: true));
```

That flag acknowledges **redirects only**. Pinning is still installed when the terminal type exposes a certificate callback; a type that does not expose one cannot be pinned by the layer and is logged at error level, in which case you must enforce the Approov pins yourself with `ApproovService.VerifyServerTrust`.

The default (parameterless) constructor is recommended on iOS because it has access to the original native trust object.

## CHECKING IT WORKS

Initially you won't have set which API domains to protect, so the layer will not add anything. It will have called Approov though and made contact with the Approov cloud service. You will see logging from Approov saying `UNKNOWN_URL`.

Your Approov onboarding email should contain a link allowing you to access [Live Metrics Graphs](https://approov.io/docs/latest/approov-usage-documentation/#metrics-graphs). After you've run your app with Approov integration you should be able to see the results in the live metrics within a minute or so. At this stage you could even release your app to get details of your app population and the attributes of the devices they are running upon.

## NEXT STEPS

To actually protect your APIs and/or secrets there are some further steps. Approov provides two different options for protection:

* **API PROTECTION**: You should use this if you control the backend API(s) being protected and are able to modify them to ensure that a valid Approov token is being passed by the app. An [Approov Token](https://approov.io/docs/latest/approov-usage-documentation/#approov-tokens) is short lived cryptographically signed JWT proving the authenticity of the call.

* **SECRETS PROTECTION**: This allows app secrets, including API keys for 3rd party services, to be protected so that they no longer need to be included in the released app code. These secrets are only made available to valid apps at runtime.

Note that it is possible to use both approaches side-by-side in the same app.

Please see the [MAUI HttpClient](https://github.com/approov/quickstart-maui-httpclient) and [MAUI Refit](https://github.com/approov/quickstart-maui-refit) quickstarts for worked examples.

## REPOSITORY LAYOUT

- `ApproovService.MAUI` — the service layer, multi-targeting `net9.0-android` and `net9.0-ios`
- `iOS.Binding` — Objective-C binding project over the Approov iOS SDK
- `libs/` — where you place `approov.aar`; see [libs/README.md](libs/README.md)
- `iOS.Binding/libs/` — where you place `Approov.xcframework`; see [iOS.Binding/libs/README.md](iOS.Binding/libs/README.md)
- `ApproovService.MAUI.Tests` — xUnit suite over the shared, platform-independent code

# Interface

Please see the [REFERENCE.md](REFERENCE.md) for more information on the Approov Service for .NET MAUI, including the deliberate divergences from the other Approov service layers.

# Usage

Please see the [USAGE.md](USAGE.md) for more information on how to use this wrapper.

# Changelog

Please see the [CHANGELOG.md](CHANGELOG.md) for more information on the changes in each version.

# Migration

Migrating from `approov-service-xamarin`? Please see the [MIGRATION.md](MIGRATION.md).
