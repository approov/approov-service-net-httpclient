# Approov iOS SDK (`Approov.xcframework`) goes here

This repository does **not** ship the Approov SDK. The SDK is Approov's proprietary,
closed-source product and is not covered by this repository's MIT `LICENSE`; you obtain it
from your own Approov account. You must place it here yourself before building the iOS
target — the Objective-C binding project links against it and **cannot compile without it**.

## What is required

| Item | Value |
|---|---|
| Artifact | `Approov.xcframework` (a directory, not a file) |
| Location | `iOS.Binding/libs/Approov.xcframework` (this directory) |
| Referenced by | `iOS.Binding/iOS.Binding.csproj` — `<NativeReference Include="libs/Approov.xcframework">` |
| SDK version this service layer was tested against | **3.5.3** |

The name and location are what the build expects — do not rename it or move it. Keep the
xcframework intact, including its `PrivacyInfo.xcprivacy` files and code signature; both the
device slice (`ios-arm64`) and the simulator slice (`ios-arm64_x86_64-simulator`) are needed
to build for device and simulator.

## How to obtain it

Use the [`approov` CLI](https://approov.io/docs/latest/approov-installation/) with a trial or
paid Approov account (`approov` must already be authenticated with your account's management
token):

```bash
# from the repository root
approov sdk -getLibrary iOS.Binding/libs/Approov.xcframework
```

`-getLibrary` writes the iOS SDK; with an `.xcframework` path it produces the xcframework
containing the device and simulator slices. It returns the SDK version currently selected for
your account. To see what is available, or to fetch a specific version:

```bash
approov sdk -list                                                          # available SDK versions and library IDs
approov sdk -getLibrary iOS.Binding/libs/Approov.xcframework -libraryID <id>
```

## Verifying what you got

```bash
plutil -p iOS.Binding/libs/Approov.xcframework/ios-arm64/Approov.framework/Info.plist \
  | grep CFBundleShortVersionString
ls iOS.Binding/libs/Approov.xcframework          # expect Info.plist + both slice directories
```

Expect `"CFBundleShortVersionString" => "3.5.3"` to match the tested version above.

## Version drift warning (iOS-specific)

`ApiDefinition.cs` and `StructsAndEnums.cs` in the parent directory are **Objective
Sharpie**-generated C# descriptions of the SDK's Objective-C API, and they are checked in
against the **3.5.3** headers. If you drop in an xcframework whose API differs:

- a removed or renamed selector shows up as a **link/build failure** in `iOS.Binding`; but
- an **added** API is simply missing from the binding, and a **changed signature** can bind
  incorrectly without any build error.

So a build that succeeds is not by itself proof that the binding matches the SDK version you
supplied. If you must use a version other than 3.5.3, regenerate the binding with Objective
Sharpie against that xcframework's headers and diff it against the committed files.

Automating that check in CI is planned.

## Why the binary is not committed

The SDK is Approov's proprietary product and this repository is MIT-licensed wrapper source, so the
two cannot ship together. Distributing the SDK as a NuGet package, which removes this manual step, is
planned; packing is disabled on this project until then, because a package built from it would embed
the proprietary xcframework.
