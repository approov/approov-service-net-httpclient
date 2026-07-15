// ApproovService.MAUI/Platforms/Android/ApproovServicePlatform.cs
using System.Security.Cryptography.X509Certificates;
using Com.Criticalblue.Approovsdk;
using Java.Security.Cert;

namespace Approov;

public static partial class ApproovService
{
    private static partial bool PlatformInitializeSdk(string config, string? comment)
    {
        var context = Android.App.Application.Context;
        string? initial = config; string? update = null;
        int ci = config.IndexOf(':');
        if (ci >= 0) { initial = config[..ci]; update = config[(ci + 1)..]; }
        try
        {
            // false means the SDK is already initialized, which is not a failure
            return global::Com.Criticalblue.Approovsdk.Approov.Initialize(
                context, initial, update, comment);
        }
        catch (Java.Lang.Exception ex)
        {
            throw new InitializationFailureException(
                $"Approov SDK initialization failed: {ex.Message}");
        }
    }

    private static partial void PlatformSetUserProperty(string property)
        => global::Com.Criticalblue.Approovsdk.Approov.SetUserProperty(property);

    private static partial IApproovTokenFetchResult PlatformFetchApproovTokenAndWait(string url)
        => RequireTokenFetchResult(
            global::Com.Criticalblue.Approovsdk.Approov.FetchApproovTokenAndWait(url),
            "fetching an Approov token");

    private static partial IApproovTokenFetchResult PlatformFetchSecureStringAndWait(string key, string? newDef)
        => RequireTokenFetchResult(
            global::Com.Criticalblue.Approovsdk.Approov.FetchSecureStringAndWait(key, newDef),
            "fetching a secure string");

    private static partial IApproovTokenFetchResult PlatformFetchCustomJWTAndWait(string payload)
        => RequireTokenFetchResult(
            global::Com.Criticalblue.Approovsdk.Approov.FetchCustomJWTAndWait(payload),
            "fetching a custom JWT");

    private static AndroidTokenFetchResult RequireTokenFetchResult(
        global::Com.Criticalblue.Approovsdk.Approov.TokenFetchResult? result,
        string operation)
        => result == null
            ? throw new PermanentException($"Approov SDK returned no result while {operation}")
            : new AndroidTokenFetchResult(result);

    private static partial void PlatformSetDataHashInToken(string data)
        => global::Com.Criticalblue.Approovsdk.Approov.SetDataHashInToken(data);

    private static partial void PlatformSetDevKey(string devKey)
        => global::Com.Criticalblue.Approovsdk.Approov.SetDevKey(devKey);

    private static partial string? PlatformGetDeviceID()
        => global::Com.Criticalblue.Approovsdk.Approov.DeviceID;

    private static partial string? PlatformGetAccountMessageSignature(string message)
        => global::Com.Criticalblue.Approovsdk.Approov.GetAccountMessageSignature(message);

    private static partial string? PlatformGetInstallMessageSignature(string message)
        => global::Com.Criticalblue.Approovsdk.Approov.GetInstallMessageSignature(message);

    private static partial string? PlatformGetPinsJSON(string pinType)
        => global::Com.Criticalblue.Approovsdk.Approov.GetPinsJSON(pinType);

    private static partial string? PlatformFetchConfig()
        => global::Com.Criticalblue.Approovsdk.Approov.FetchConfig();

    private static partial byte[]? PlatformExtractPublicKeyBytes(X509Certificate2 cert)
    {
        try
        {
            var cf = CertificateFactory.GetInstance("X.509")!;
            using var ms = new System.IO.MemoryStream(cert.RawData);
            var jc = cf.GenerateCertificate(ms) as Java.Security.Cert.X509Certificate;
            return jc?.PublicKey?.GetEncoded();
        }
        catch (Exception ex)
        {
            Log(ApproovLogLevel.Error, $"PlatformExtractPublicKeyBytes (Android): {ex.Message}");
            return null;
        }
    }
}

internal sealed class AndroidTokenFetchResult : IApproovTokenFetchResult
{
    private readonly global::Com.Criticalblue.Approovsdk.Approov.TokenFetchResult _r;
    internal AndroidTokenFetchResult(global::Com.Criticalblue.Approovsdk.Approov.TokenFetchResult r) => _r = r;

    public ApproovTokenFetchStatus Status => _r.Status?.Name() switch
    {
        "SUCCESS" => ApproovTokenFetchStatus.Success,
        "NO_NETWORK" => ApproovTokenFetchStatus.NoNetwork,
        "MITM_DETECTED" => ApproovTokenFetchStatus.MitmDetected,
        "POOR_NETWORK" => ApproovTokenFetchStatus.PoorNetwork,
        "DISABLED" => ApproovTokenFetchStatus.Disabled,
        "UNKNOWN_KEY" => ApproovTokenFetchStatus.UnknownKey,
        "REJECTED" => ApproovTokenFetchStatus.Rejected,
        "UNKNOWN_URL" => ApproovTokenFetchStatus.UnknownUrl,
        "UNPROTECTED_URL" => ApproovTokenFetchStatus.UnprotectedUrl,
        "NO_APPROOV_SERVICE" => ApproovTokenFetchStatus.NoApproovService,
        "BAD_PAYLOAD" => ApproovTokenFetchStatus.BadPayload,
        _ => ApproovTokenFetchStatus.InternalError
    };
    public string Token => _r.Token ?? "";
    public string? SecureString => _r.SecureString;
    public string ARC => _r.ARC ?? "";
    public string RejectionReasons => _r.RejectionReasons ?? "";
    public bool IsConfigChanged => _r.IsConfigChanged;
    public bool IsForceApplyPins => _r.IsForceApplyPins;
    public string LoggableToken => _r.LoggableToken ?? "";
    public string? TraceID => _r.TraceID;
}
