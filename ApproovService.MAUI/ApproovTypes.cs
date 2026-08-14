using System.Net.Http;

namespace Approov;

public enum ApproovLogLevel { Off = 0, Error = 1, Warning = 2, Info = 3, Debug = 4 }

public enum ApproovFetchDecision { ShouldProceed, ShouldRetry, ShouldFail, ShouldIgnore }

public enum ApproovTokenFetchStatus
{
    Success = 0, NoNetwork = 1, MitmDetected = 2, PoorNetwork = 3,
    Disabled = 4, UnknownKey = 5, Rejected = 6, UnknownUrl = 7,
    UnprotectedUrl = 8, NoApproovService = 9, BadPayload = 10, InternalError = 11
}

public interface IApproovTokenFetchResult
{
    ApproovTokenFetchStatus Status { get; }
    string Token { get; }
    string? SecureString { get; }
    string ARC { get; }
    string RejectionReasons { get; }
    bool IsConfigChanged { get; }
    bool IsForceApplyPins { get; }
    string LoggableToken { get; }
    string? TraceID { get; }
}

/// <summary>
/// Immutable snapshot of a token-fetch result, taken once at the fetch boundary.
///
/// On .NET Android the SDK's <c>TokenFetchStatus</c> binds as a <c>Java.Lang.Enum</c>
/// (not a C# value enum), so its managed peer is marshalled across JNI on every access.
/// Reading the live result repeatedly — or comparing enum peers by reference — is
/// timing-sensitive and is the cause of the intermittent "Unknown approov token fetch
/// result SUCCESS" reported on .NET Android (the status appears unmatched on one read yet
/// stringifies to "SUCCESS"). Reading every field exactly once, on the calling thread
/// immediately after the synchronous native fetch and before any continuation, removes
/// that surface. All consumers then read from these immutable fields.
/// </summary>
public sealed class SnapshotTokenFetchResult : IApproovTokenFetchResult
{
    public ApproovTokenFetchStatus Status { get; }
    public string Token { get; }
    public string? SecureString { get; }
    public string ARC { get; }
    public string RejectionReasons { get; }
    public bool IsConfigChanged { get; }
    public bool IsForceApplyPins { get; }
    public string LoggableToken { get; }
    public string? TraceID { get; }

    public SnapshotTokenFetchResult(IApproovTokenFetchResult source)
    {
        // Read each member exactly once, in order, on this thread.
        Status = source.Status;
        Token = source.Token;
        SecureString = source.SecureString;
        ARC = source.ARC;
        RejectionReasons = source.RejectionReasons;
        IsConfigChanged = source.IsConfigChanged;
        IsForceApplyPins = source.IsForceApplyPins;
        LoggableToken = source.LoggableToken;
        TraceID = source.TraceID;
    }

    /// <summary>Snapshot a fetch result, unless it is already an immutable snapshot.</summary>
    public static IApproovTokenFetchResult Of(IApproovTokenFetchResult source) =>
        source is SnapshotTokenFetchResult ? source : new SnapshotTokenFetchResult(source);
}

public struct ApproovUpdateResponse
{
    public HttpRequestMessage? Request;
    public ApproovFetchDecision Decision;
    public string? SdkMessage;
    public Exception? Error;
}

public class ApproovException : Exception
{
    public bool ShouldRetry { get; }
    public ApproovException(string message, bool shouldRetry = false)
        : base(message) => ShouldRetry = shouldRetry;
    public ApproovException(string message, Exception inner, bool shouldRetry = false)
        : base(message, inner) => ShouldRetry = shouldRetry;
}

public class InitializationFailureException : ApproovException
{
    public InitializationFailureException(string message) : base(message) { }
}

public class ConfigurationFailureException : ApproovException
{
    public ConfigurationFailureException(string message) : base(message) { }
}

public class PinningErrorException : ApproovException
{
    public PinningErrorException(string message) : base(message) { }
}

public class NetworkingErrorException : ApproovException
{
    public NetworkingErrorException(string message, bool shouldRetry = true)
        : base(message, shouldRetry) { }
}

public class PermanentException : ApproovException
{
    public PermanentException(string message) : base(message) { }
    public PermanentException(string message, Exception inner) : base(message, inner) { }
}

public class RejectionException : ApproovException
{
    public string ARC { get; }
    public string RejectionReasons { get; }
    public RejectionException(string message, string arc = "", string rejectionReasons = "")
        : base(message) { ARC = arc; RejectionReasons = rejectionReasons; }
}
