using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Approov;

using ApproovNative = global::ApproovSDK.Approov;

public static partial class ApproovService
{
    private static partial bool PlatformInitializeSdk(string config, string? comment)
    {
        string? initial = config; string? update = null;
        int ci = config.IndexOf(':');
        if (ci >= 0) { initial = config[..ci]; update = config[(ci + 1)..]; }
        bool ok = ApproovNative.Initialize(initial, update, comment, out var err);
        if (err != null)
            throw new InitializationFailureException(
                $"Approov SDK init error: {err.LocalizedDescription}");
        // ok == false with no error means the SDK is already initialized
        return ok;
    }

    private static partial void PlatformSetUserProperty(string property)
        => ApproovNative.SetUserProperty(property);

    private static partial IApproovTokenFetchResult PlatformFetchApproovTokenAndWait(string url)
        => new iOSTokenFetchResult(ApproovNative.FetchApproovTokenAndWait(url));

    private static partial IApproovTokenFetchResult PlatformFetchSecureStringAndWait(string key, string? newDef)
        => new iOSTokenFetchResult(ApproovNative.FetchSecureStringAndWait(key, newDef));

    private static partial IApproovTokenFetchResult PlatformFetchCustomJWTAndWait(string payload)
        => new iOSTokenFetchResult(ApproovNative.FetchCustomJWTAndWait(payload));

    private static partial void PlatformSetDataHashInToken(string data)
        => ApproovNative.SetDataHashInToken(data);

    private static partial void PlatformSetDevKey(string devKey)
        => ApproovNative.SetDevKey(devKey);

    private static partial string? PlatformGetDeviceID()
        => ApproovNative.DeviceID();

    private static partial string? PlatformGetAccountMessageSignature(string message)
        => ApproovNative.GetAccountMessageSignature(message);

    private static partial string? PlatformGetInstallMessageSignature(string message)
        => ApproovNative.GetInstallMessageSignature(message);

    private static partial string? PlatformGetPinsJSON(string pinType)
        => ApproovNative.GetPinsJSON(pinType);

    private static partial string? PlatformFetchConfig()
        => ApproovNative.FetchConfig();

    private static partial byte[]? PlatformExtractPublicKeyBytes(X509Certificate2 cert)
    {
        try
        {
            using var rsa = cert.GetRSAPublicKey();
            if (rsa != null) return rsa.ExportSubjectPublicKeyInfo();
            using var ecdsa = cert.GetECDsaPublicKey();
            if (ecdsa != null) return ecdsa.ExportSubjectPublicKeyInfo();
            return null;
        }
        catch (Exception ex)
        {
            Log(ApproovLogLevel.Error, $"PlatformExtractPublicKeyBytes (iOS): {ex.Message}");
            return null;
        }
    }
}

internal sealed class iOSTokenFetchResult : IApproovTokenFetchResult
{
    private readonly ApproovSDK.ApproovTokenFetchResult _r;
    internal iOSTokenFetchResult(ApproovSDK.ApproovTokenFetchResult r) => _r = r;

    public ApproovTokenFetchStatus Status => IOSTokenFetchStatusMapper.Map(_r.Status());
    public string Token => _r.Token() ?? "";
    public string? SecureString => _r.SecureString();
    public string ARC => _r.ARC() ?? "";
    public string RejectionReasons => _r.RejectionReasons() ?? "";
    public bool IsConfigChanged => _r.IsConfigChanged();
    public bool IsForceApplyPins => _r.IsForceApplyPins();
    public string LoggableToken => _r.LoggableToken() ?? "";
    public string? TraceID => _r.TraceID();
}
