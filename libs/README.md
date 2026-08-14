# Approov Android SDK (`approov.aar`) goes here

This repository does **not** ship the Approov SDK. The SDK is Approov's proprietary,
closed-source product and is not covered by this repository's MIT `LICENSE`; you obtain it
from your own Approov account. You must place it here yourself before building the Android
target.

## What is required

| Item | Value |
|---|---|
| File | `approov.aar` |
| Location | `libs/approov.aar` (this directory) |
| Referenced by | `ApproovService.MAUI/ApproovService.MAUI.csproj` — `<AndroidLibrary Include="../libs/approov.aar" />` |
| SDK version this service layer was tested against | **3.5.3** |

The file name and location are what the build expects — do not rename it or move it.

## How to obtain it

Use the [`approov` CLI](https://approov.io/docs/latest/approov-installation/) with a trial or
paid Approov account (`approov` must already be authenticated with your account's management
token):

```bash
# from the repository root
approov sdk -getLibrary libs/approov.aar
```

`-getLibrary` writes the Android SDK as an `.aar`. It returns the SDK version currently
selected for your account. To see what is available, or to fetch a specific version:

```bash
approov sdk -list                                        # lists available SDK versions and their library IDs
approov sdk -getLibrary libs/approov.aar -libraryID <id>  # fetch a specific SDK library
```

## Verifying what you got

```bash
unzip -p libs/approov.aar AndroidManifest.xml | grep versionName
```

Expect `android:versionName="3.5.3"` to match the tested version above. A different version
is likely to work, but only 3.5.3 has been tested with this service layer — if you use
another version and see a behavioural difference, mention the version when reporting it.

## Why the binary is not committed

The SDK is Approov's proprietary product and this repository is MIT-licensed wrapper source, so the
two cannot ship together. Distributing the SDK as a NuGet package, which removes this manual step, is
planned.
