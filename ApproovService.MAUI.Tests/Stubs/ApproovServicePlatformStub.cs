// ApproovService.MAUI.Tests/Stubs/ApproovServicePlatformStub.cs
using System.Security.Cryptography.X509Certificates;
using Approov.Tests;

namespace Approov;

public static partial class ApproovService
{
    // Token fetch stub controls (pre-existing)
    internal static StubTokenFetchResult? NextFetchResult = null;
    internal static int FetchCallCount = 0;

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

    // New: secure-string result override (for substitution edge-case tests)
    internal static StubTokenFetchResult? NextSecureStringResult = null;

    // New: custom JWT call tracking (for bypass guard tests)
    internal static int CustomJWTCallCount = 0;

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
        if (NextFetchResult != null) { var r = NextFetchResult; NextFetchResult = null; return r; }
        return new StubTokenFetchResult { Status = ApproovTokenFetchStatus.Success, Token = "stub-token" };
    }

    private static partial IApproovTokenFetchResult PlatformFetchSecureStringAndWait(string key, string? newDef)
    {
        if (NextSecureStringResult != null) { var r = NextSecureStringResult; NextSecureStringResult = null; return r; }
        return new StubTokenFetchResult { Status = ApproovTokenFetchStatus.Success, SecureString = "stub-secret" };
    }

    private static partial IApproovTokenFetchResult PlatformFetchCustomJWTAndWait(string payload)
    {
        CustomJWTCallCount++;
        return new StubTokenFetchResult { Status = ApproovTokenFetchStatus.Success, Token = "stub-jwt" };
    }

    private static partial void PlatformSetDataHashInToken(string data)
    {
        SetDataHashCallCount++;
        LastDataHashValue = data;
    }

    private static partial void PlatformSetDevKey(string devKey) { }
    private static partial string? PlatformGetDeviceID() => "test-device-id";
    private static partial string? PlatformGetAccountMessageSignature(string message) => null;
    private static partial string? PlatformGetInstallMessageSignature(string message) => null;
    private static partial string? PlatformGetPinsJSON(string pinType) => null;
    private static partial string? PlatformFetchConfig() => null;
    private static partial byte[]? PlatformExtractPublicKeyBytes(X509Certificate2 cert) => null;
}
