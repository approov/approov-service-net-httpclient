// ApproovService.MAUI.Tests/Stubs/ApproovServicePlatformStub.cs
using System.Security.Cryptography.X509Certificates;
using Approov.Tests;

namespace Approov;

public static partial class ApproovService
{
    // Token fetch stub controls (pre-existing)
    internal static StubTokenFetchResult? NextFetchResult = null;
    internal static int FetchCallCount = 0;
    internal static string? LastFetchUrl = null;
    // When set, the next platform token fetch throws this exception (one-shot).
    internal static Exception? NextFetchException = null;

    // New: SDK init tracking
    internal static int InitCallCount = 0;
    internal static bool NextInitShouldThrow = false;
    internal static bool NextInitReturnsFalse = false;
    internal static string? LastInitComment = null;
    internal static bool LastInitCommentWasNull = false;

    // New: user-property tracking (for telemetry tests)
    internal static string? LastUserProperty = null;

    // New: data-hash tracking (for binding header tests)
    internal static int SetDataHashCallCount = 0;
    internal static string? LastDataHashValue = null;
    internal static bool BlockTokenFetch = false;
    internal static ManualResetEventSlim TokenFetchStarted = new(false);
    internal static ManualResetEventSlim ReleaseTokenFetch = new(false);

    // New: secure-string result override (for substitution edge-case tests)
    internal static StubTokenFetchResult? NextSecureStringResult = null;
    internal static int SecureStringCallCount = 0;
    internal static string? LastSecureStringKey = null;
    internal static string? LastSecureStringNewDef = null;

    // New: custom JWT call tracking (for bypass guard tests)
    internal static int CustomJWTCallCount = 0;
    internal static StubTokenFetchResult? NextCustomJWTResult = null;
    internal static string? LastCustomJWTPayload = null;

    // New: SDK helper call tracking
    internal static int DevKeyCallCount = 0;
    internal static string? LastDevKey = null;
    internal static int AccountSignatureCallCount = 0;
    internal static int InstallSignatureCallCount = 0;
    internal static string? AccountSignatureResult = null;
    internal static string? InstallSignatureResult = null;
    internal static string? LastAccountSignatureMessage = null;
    internal static string? LastInstallSignatureMessage = null;
    internal static string? FetchConfigResult = null;

    // New: pinning controls
    internal static string? PinsJson = null;
    internal static string? LastPinType = null;
    internal static byte[]? PublicKeyBytes = null;
    // When true, public-key extraction fails for every certificate (cannot pin it).
    internal static bool ExtractReturnsNull = false;

    internal static void ResetPlatformStub()
    {
        NextFetchResult = null;
        FetchCallCount = 0;
        LastFetchUrl = null;
        NextFetchException = null;
        InitCallCount = 0;
        NextInitShouldThrow = false;
        NextInitReturnsFalse = false;
        LastInitComment = null;
        LastInitCommentWasNull = false;
        LastUserProperty = null;
        SetDataHashCallCount = 0;
        LastDataHashValue = null;
        BlockTokenFetch = false;
        TokenFetchStarted.Reset();
        ReleaseTokenFetch.Set();
        NextSecureStringResult = null;
        SecureStringCallCount = 0;
        LastSecureStringKey = null;
        LastSecureStringNewDef = null;
        CustomJWTCallCount = 0;
        NextCustomJWTResult = null;
        LastCustomJWTPayload = null;
        DevKeyCallCount = 0;
        LastDevKey = null;
        AccountSignatureCallCount = 0;
        InstallSignatureCallCount = 0;
        AccountSignatureResult = null;
        InstallSignatureResult = null;
        LastAccountSignatureMessage = null;
        LastInstallSignatureMessage = null;
        FetchConfigResult = null;
        PinsJson = null;
        LastPinType = null;
        PublicKeyBytes = null;
        ExtractReturnsNull = false;
    }

    private static partial bool PlatformInitializeSdk(string config, string? comment)
    {
        InitCallCount++;
        LastInitComment = comment;
        LastInitCommentWasNull = comment == null;
        if (NextInitShouldThrow)
        {
            NextInitShouldThrow = false;
            throw new Exception("Stub: PlatformInitializeSdk forced failure");
        }
        if (NextInitReturnsFalse)
        {
            NextInitReturnsFalse = false;
            return false;
        }
        return true;
    }

    private static partial void PlatformSetUserProperty(string property)
    {
        LastUserProperty = property;
    }

    private static partial IApproovTokenFetchResult PlatformFetchApproovTokenAndWait(string url)
    {
        FetchCallCount++;
        LastFetchUrl = url;
        TokenFetchStarted.Set();
        if (BlockTokenFetch) ReleaseTokenFetch.Wait();
        if (NextFetchException != null) { var e = NextFetchException; NextFetchException = null; throw e; }
        if (NextFetchResult != null) { var r = NextFetchResult; NextFetchResult = null; return r; }
        return new StubTokenFetchResult { Status = ApproovTokenFetchStatus.Success, Token = "stub-token" };
    }

    private static partial IApproovTokenFetchResult PlatformFetchSecureStringAndWait(string key, string? newDef)
    {
        SecureStringCallCount++;
        LastSecureStringKey = key;
        LastSecureStringNewDef = newDef;
        if (NextSecureStringResult != null) { var r = NextSecureStringResult; NextSecureStringResult = null; return r; }
        return new StubTokenFetchResult { Status = ApproovTokenFetchStatus.Success, SecureString = "stub-secret" };
    }

    private static partial IApproovTokenFetchResult PlatformFetchCustomJWTAndWait(string payload)
    {
        CustomJWTCallCount++;
        LastCustomJWTPayload = payload;
        if (NextCustomJWTResult != null) { var r = NextCustomJWTResult; NextCustomJWTResult = null; return r; }
        return new StubTokenFetchResult { Status = ApproovTokenFetchStatus.Success, Token = "stub-jwt" };
    }

    private static partial void PlatformSetDataHashInToken(string data)
    {
        SetDataHashCallCount++;
        LastDataHashValue = data;
    }

    private static partial void PlatformSetDevKey(string devKey)
    {
        DevKeyCallCount++;
        LastDevKey = devKey;
    }

    private static partial string? PlatformGetDeviceID() => "test-device-id";

    private static partial string? PlatformGetAccountMessageSignature(string message)
    {
        AccountSignatureCallCount++;
        LastAccountSignatureMessage = message;
        return AccountSignatureResult;
    }

    private static partial string? PlatformGetInstallMessageSignature(string message)
    {
        InstallSignatureCallCount++;
        LastInstallSignatureMessage = message;
        return InstallSignatureResult;
    }

    private static partial string? PlatformGetPinsJSON(string pinType)
    {
        LastPinType = pinType;
        return PinsJson;
    }

    private static partial string? PlatformFetchConfig() => FetchConfigResult;

    // When PublicKeyBytes is set, return it for every certificate (single-cert tests).
    // Otherwise return the certificate's real DER SubjectPublicKeyInfo, matching the
    // native platforms (Java publicKey.GetEncoded() / RSA|ECDsa.ExportSubjectPublicKeyInfo),
    // so distinct chain elements yield distinct pins.
    private static partial byte[]? PlatformExtractPublicKeyBytes(X509Certificate2 cert)
        => ExtractReturnsNull ? null : (PublicKeyBytes ?? cert.PublicKey.ExportSubjectPublicKeyInfo());
}
