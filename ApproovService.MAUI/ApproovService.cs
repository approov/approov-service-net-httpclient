// ApproovService.MAUI/ApproovService.cs
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;

namespace Approov;

public static partial class ApproovService
{
    // Locks
    private static readonly object _initLock = new();
    private static readonly object _stateLock = new();
    private static readonly object _loggingLock = new();
    private static readonly object _failureCacheLock = new();

    // Initialization state
    private static bool _sdkInitialized = false;
    private static string? _configUsed = null;
    private static bool _isBypassMode = false;

    // Token configuration
    private static string _approovTokenHeader = "Approov-Token";
    private static string _approovTokenPrefix = "";
    private static string? _approovTraceIDHeader = null;
    private static string? _bindingHeader = null;
    private static bool _useApproovStatusIfNoToken = false;
    private static bool _bodyDigestEnabled = true;
    private static bool _bodyDigestRequired = false;

    // Service wiring
    private static IApproovServiceMutator _serviceMutator = ApproovServiceMutatorDefault.Shared;

    // Substitution tables
    private static Dictionary<string, string> _substitutionHeaders = new();
    private static HashSet<string> _substitutionQueryParams = new();
    private static Dictionary<string, Regex> _exclusionURLRegexs = new();

    // Audit / logging
    private static string _lastARC = "";
    private static ApproovLogLevel _loggingLevel = ApproovLogLevel.Info;

    // Failure cache
    private static IApproovTokenFetchResult? _failureCacheResult = null;
    private static DateTime _failureCacheExpiry = DateTime.MinValue;
    private static double _failureCacheTTL = 5.0;
    private static ManualResetEventSlim? _failureCacheMissGroup = null;

    // Partial method declarations (resolved by Platforms/Android or Platforms/iOS)
    // Returns true if newly initialized, false if the platform SDK reports it is
    // already initialized (treated as success); throws on real failure
    private static partial bool PlatformInitializeSdk(string config, string? comment);
    private static partial void PlatformSetUserProperty(string property);
    private static partial IApproovTokenFetchResult PlatformFetchApproovTokenAndWait(string url);
    private static partial IApproovTokenFetchResult PlatformFetchSecureStringAndWait(string key, string? newDef);
    private static partial IApproovTokenFetchResult PlatformFetchCustomJWTAndWait(string payload);
    private static partial void PlatformSetDataHashInToken(string data);
    private static partial void PlatformSetDevKey(string devKey);
    private static partial string? PlatformGetDeviceID();
    private static partial string? PlatformGetAccountMessageSignature(string message);
    private static partial string? PlatformGetInstallMessageSignature(string message);
    private static partial string? PlatformGetPinsJSON(string pinType);
    private static partial string? PlatformFetchConfig();
    private static partial byte[]? PlatformExtractPublicKeyBytes(X509Certificate2 cert);

    // Bypass result — returned for any SDK call in bypass mode
    private sealed record BypassFetchResult(ApproovTokenFetchStatus Status) : IApproovTokenFetchResult
    {
        public string Token => "";
        public string? SecureString => null;
        public string ARC => "";
        public string RejectionReasons => "";
        public bool IsConfigChanged => false;
        public bool IsForceApplyPins => false;
        public string LoggableToken => "";
        public string? TraceID => null;
    }

    public static void Initialize(string config, string? comment = null)
    {
        lock (_initLock)
        {
            if (string.IsNullOrEmpty(config))
            {
                if (_sdkInitialized)
                {
                    Log(ApproovLogLevel.Info,
                        "Initialize with empty configuration ignored: already initialized");
                    return;
                }
                _configUsed = config;
                _isBypassMode = true;
                _sdkInitialized = true;
                Log(ApproovLogLevel.Info, "ApproovService initialized in bypass mode");
                return;
            }

            // Non-empty configs are always forwarded to the platform SDK; a throw
            // propagates before any service-layer state is modified
            bool newlyInitialized = PlatformInitializeSdk(config, comment);
            if (!newlyInitialized)
                Log(ApproovLogLevel.Info,
                    "Platform SDK reports already initialized: treated as success");
            PlatformSetUserProperty("approov-service-maui/3.5.11");

            // Platform success: reset and re-commit service-layer state
            lock (_stateLock)
            {
                _approovTokenHeader = "Approov-Token"; _approovTokenPrefix = "";
                _approovTraceIDHeader = null; _bindingHeader = null;
                _useApproovStatusIfNoToken = false; _bodyDigestEnabled = true;
                _bodyDigestRequired = false;
                _serviceMutator = ApproovServiceMutatorDefault.Shared;
                _substitutionHeaders = new(); _substitutionQueryParams = new();
                _exclusionURLRegexs = new();
            }
            _configUsed = config;
            _isBypassMode = false;
            _sdkInitialized = true;
            Log(ApproovLogLevel.Info, "ApproovService initialized");
        }
    }

    public static bool IsInitialized()
    {
        lock (_initLock) { return _sdkInitialized; }
    }

    public static bool IsApproovEnabled()
    {
        lock (_initLock) { return _sdkInitialized && !_isBypassMode; }
    }

    private static void EnsureInitialized()
    {
        if (!_sdkInitialized)
            throw new InitializationFailureException("ApproovService has not been initialized");
    }

    public static void SetLoggingLevel(ApproovLogLevel level)
    {
        lock (_loggingLock) { _loggingLevel = level; }
    }

    internal static void Log(ApproovLogLevel level, string message)
    {
        ApproovLogLevel current;
        lock (_loggingLock) { current = _loggingLevel; }
        if (level <= current)
            System.Diagnostics.Debug.WriteLine($"[Approov] [{level}] {message}");
    }

    public static void SetApproovTokenHeader(string header, string prefix = "")
    {
        lock (_stateLock) { _approovTokenHeader = header; _approovTokenPrefix = prefix; }
    }

    public static (string header, string prefix) GetApproovTokenHeader()
    {
        lock (_stateLock) { return (_approovTokenHeader, _approovTokenPrefix); }
    }

    public static void SetApproovTraceIDHeader(string? header)
    {
        lock (_stateLock) { _approovTraceIDHeader = header; }
    }

    public static string? GetApproovTraceIDHeader()
    {
        lock (_stateLock) { return _approovTraceIDHeader; }
    }

    public static void SetBindingHeader(string? header)
    {
        lock (_stateLock) { _bindingHeader = header; }
    }

    public static string? GetBindingHeader()
    {
        lock (_stateLock) { return _bindingHeader; }
    }

    public static void SetUseApproovStatusIfNoToken(bool use)
    {
        lock (_stateLock) { _useApproovStatusIfNoToken = use; }
    }

    public static void SetBodyDigestEnabled(bool enabled)
    {
        SetBodyDigestEnabled(enabled, false);
    }

    // Configures Content-Digest generation. When required is true (and the digest is
    // enabled) a POST/PUT/PATCH request whose body cannot be digested (one-shot
    // streaming content, or a digest computation failure) fails closed with a
    // PermanentException instead of being sent without a Content-Digest header.
    // A request with no body never fails: there is nothing to digest.
    // required is only meaningful while enabled; each call fully specifies the config,
    // so SetBodyDigestEnabled(enabled) clears any previously set required mode.
    public static void SetBodyDigestEnabled(bool enabled, bool required)
    {
        lock (_stateLock)
        {
            _bodyDigestEnabled = enabled;
            _bodyDigestRequired = enabled && required;
        }
    }

    internal static bool IsBodyDigestEnabled()
    {
        lock (_stateLock) { return _bodyDigestEnabled; }
    }

    internal static bool IsBodyDigestRequired()
    {
        lock (_stateLock) { return _bodyDigestRequired; }
    }

    public static string? GetLastARC()
    {
        lock (_stateLock) { return _lastARC; }
    }

    public static void SetServiceMutator(IApproovServiceMutator? mutator)
    {
        lock (_stateLock) { _serviceMutator = mutator ?? ApproovServiceMutatorDefault.Shared; }
    }

    public static IApproovServiceMutator GetServiceMutator()
    {
        lock (_stateLock) { return _serviceMutator; }
    }

    public static void SetFailureCacheTTL(double seconds)
    {
        lock (_stateLock) { _failureCacheTTL = seconds; }
    }

    public static Dictionary<string, Regex> GetExclusionURLRegexs()
    {
        lock (_stateLock) { return new Dictionary<string, Regex>(_exclusionURLRegexs); }
    }

    internal static void ResetForTesting()
    {
        lock (_initLock) lock (_stateLock) lock (_loggingLock) lock (_failureCacheLock)
        {
            _sdkInitialized = false; _configUsed = null; _isBypassMode = false;
            _approovTokenHeader = "Approov-Token"; _approovTokenPrefix = "";
            _approovTraceIDHeader = null; _bindingHeader = null;
            _useApproovStatusIfNoToken = false; _bodyDigestEnabled = true;
            _bodyDigestRequired = false;
            _serviceMutator = ApproovServiceMutatorDefault.Shared;
            _substitutionHeaders = new(); _substitutionQueryParams = new();
            _exclusionURLRegexs = new(); _lastARC = "";
            _loggingLevel = ApproovLogLevel.Info;
            _failureCacheResult = null; _failureCacheExpiry = DateTime.MinValue;
            _failureCacheTTL = 5.0; _failureCacheMissGroup = null;
        }
    }
}

public static partial class ApproovService
{
    // === Substitution management ===

    public static void AddSubstitutionHeader(string header, string? requiredPrefix)
    {
        lock (_stateLock)
        {
            _substitutionHeaders[header] = requiredPrefix ?? "";
            Log(ApproovLogLevel.Info, $"AddSubstitutionHeader: {header}");
        }
    }

    public static void RemoveSubstitutionHeader(string header)
    {
        lock (_stateLock) { _substitutionHeaders.Remove(header); }
    }

    public static Dictionary<string, string> GetSubstitutionHeaders()
    {
        lock (_stateLock) { return new Dictionary<string, string>(_substitutionHeaders); }
    }

    public static void AddSubstitutionQueryParam(string key)
    {
        lock (_stateLock)
        {
            _substitutionQueryParams.Add(key);
            Log(ApproovLogLevel.Info, $"AddSubstitutionQueryParam: {key}");
        }
    }

    public static void RemoveSubstitutionQueryParam(string key)
    {
        lock (_stateLock) { _substitutionQueryParams.Remove(key); }
    }

    public static HashSet<string> GetSubstitutionQueryParams()
    {
        lock (_stateLock) { return new HashSet<string>(_substitutionQueryParams); }
    }

    public static void AddExclusionURLRegex(string name, string pattern)
    {
        lock (_stateLock)
        {
            _exclusionURLRegexs[name] = new Regex(pattern, RegexOptions.Compiled);
        }
    }

    public static void RemoveExclusionURLRegex(string name)
    {
        lock (_stateLock) { _exclusionURLRegexs.Remove(name); }
    }

    // === SDK operations ===

    public static void Precheck()
    {
        EnsureInitialized();
        if (_isBypassMode) return;
        var result = PlatformFetchApproovTokenAndWait("approov.io");
        IApproovServiceMutator mutator;
        lock (_stateLock) { mutator = _serviceMutator; }
        mutator.HandlePrecheckResult(result);
    }

    public static IApproovTokenFetchResult FetchApproovToken(string url)
    {
        EnsureInitialized();
        if (_isBypassMode) return new BypassFetchResult(ApproovTokenFetchStatus.UnknownUrl);
        var result = PlatformFetchApproovTokenAndWait(url);
        IApproovServiceMutator mutator;
        lock (_stateLock) { mutator = _serviceMutator; }
        mutator.HandleFetchTokenResult(result);
        return result;
    }

    public static IApproovTokenFetchResult FetchSecureString(string key, string? newDef = null)
    {
        EnsureInitialized();
        if (_isBypassMode) return new BypassFetchResult(ApproovTokenFetchStatus.UnknownKey);
        var result = PlatformFetchSecureStringAndWait(key, newDef);
        IApproovServiceMutator mutator;
        lock (_stateLock) { mutator = _serviceMutator; }
        mutator.HandleFetchSecureStringResult(result, newDef == null ? "fetch" : "set", key);
        return result;
    }

    public static IApproovTokenFetchResult FetchCustomJWT(string payload)
    {
        EnsureInitialized();
        if (_isBypassMode) return new BypassFetchResult(ApproovTokenFetchStatus.Disabled);
        var result = PlatformFetchCustomJWTAndWait(payload);
        IApproovServiceMutator mutator;
        lock (_stateLock) { mutator = _serviceMutator; }
        mutator.HandleFetchCustomJWTResult(result);
        return result;
    }

    public static string? GetDeviceID()
    {
        EnsureInitialized();
        return _isBypassMode ? null : PlatformGetDeviceID();
    }

    public static string? GetAccountMessageSignature(string message)
    {
        EnsureInitialized();
        return _isBypassMode ? null : PlatformGetAccountMessageSignature(message);
    }

    public static string? GetInstallMessageSignature(string message)
    {
        EnsureInitialized();
        return _isBypassMode ? null : PlatformGetInstallMessageSignature(message);
    }

    public static void SetDataHashInToken(string data)
    {
        EnsureInitialized();
        if (!_isBypassMode) PlatformSetDataHashInToken(data);
    }

    public static void SetDevKey(string devKey)
    {
        EnsureInitialized();
        if (!_isBypassMode) PlatformSetDevKey(devKey);
    }

    public static string? FetchConfig()
    {
        EnsureInitialized();
        return _isBypassMode ? null : PlatformFetchConfig();
    }
}

public static partial class ApproovService
{
    public static ApproovUpdateResponse UpdateRequestWithApproov(HttpRequestMessage request)
    {
        if (!_sdkInitialized)
            return new ApproovUpdateResponse { Request = request,
                Decision = ApproovFetchDecision.ShouldFail,
                SdkMessage = "ApproovService is not initialized",
                Error = new InitializationFailureException("ApproovService is not initialized") };

        if (_isBypassMode)
            return new ApproovUpdateResponse { Request = request,
                Decision = ApproovFetchDecision.ShouldIgnore };

        IApproovServiceMutator mutator;
        lock (_stateLock) { mutator = _serviceMutator; }

        try
        {
            if (!mutator.HandleInterceptorShouldProcessRequest(request))
            {
                Log(ApproovLogLevel.Info, $"UpdateRequestWithApproov: excluded {request.RequestUri}");
                return new ApproovUpdateResponse { Request = request,
                    Decision = ApproovFetchDecision.ShouldIgnore };
            }

            string url = request.RequestUri?.AbsoluteUri ?? "";

            // Binding header: hash its value into the token
            string? bindingHeader;
            lock (_stateLock) { bindingHeader = _bindingHeader; }
            if (bindingHeader != null
                && request.Headers.TryGetValues(bindingHeader, out var bindingValues))
            {
                PlatformSetDataHashInToken(string.Join(",", bindingValues));
            }

            // Fetch the Approov token via failure cache
            var tokenResult = FetchApproovTokenWithFailureCache(url);

            string tokenHeader, tokenPrefix;
            lock (_stateLock) { tokenHeader = _approovTokenHeader; tokenPrefix = _approovTokenPrefix; }

            bool useStatus;
            lock (_stateLock) { useStatus = _useApproovStatusIfNoToken; }

            bool shouldAddToken;
            try
            {
                shouldAddToken = mutator.HandleInterceptorFetchTokenResult(tokenResult, url);
                if (shouldAddToken)
                {
                    request.Headers.Remove(tokenHeader);
                    request.Headers.Add(tokenHeader, tokenPrefix + tokenResult.Token);
                }
                else if (useStatus && string.IsNullOrEmpty(tokenResult.Token))
                {
                    request.Headers.Remove(tokenHeader);
                    request.Headers.Add(tokenHeader, tokenPrefix + tokenResult.Status.ToString());
                }
            }
            catch (NetworkingErrorException) when (useStatus)
            {
                // Status injection: when flag is on, treat network errors as proceed-with-status-string
                shouldAddToken = false;
                request.Headers.Remove(tokenHeader);
                request.Headers.Add(tokenHeader, tokenPrefix + tokenResult.Status.ToString());
            }

            // TraceID header
            string? traceIDHeader;
            lock (_stateLock) { traceIDHeader = _approovTraceIDHeader; }
            if (traceIDHeader != null && tokenResult.TraceID != null)
            {
                request.Headers.Remove(traceIDHeader);
                request.Headers.Add(traceIDHeader, tokenResult.TraceID);
            }

            // Header substitutions
            var mutations = new ApproovRequestMutations
            {
                TokenHeaderKey = shouldAddToken ? tokenHeader : null,
                TraceIDHeaderKey = traceIDHeader != null && tokenResult.TraceID != null ? traceIDHeader : null,
                OriginalURL = url
            };

            Dictionary<string, string> subHeaders;
            lock (_stateLock) { subHeaders = new Dictionary<string, string>(_substitutionHeaders); }
            foreach (var (header, requiredPrefix) in subHeaders)
            {
                if (!request.Headers.TryGetValues(header, out var existingValues)) continue;
                string existing = string.Join(",", existingValues);
                if (!string.IsNullOrEmpty(requiredPrefix) && !existing.StartsWith(requiredPrefix)) continue;
                string lookupKey = string.IsNullOrEmpty(requiredPrefix)
                    ? existing : existing.Substring(requiredPrefix.Length);
                var subResult = FetchSecureStringWithFailureCache(lookupKey, null);
                if (mutator.HandleInterceptorHeaderSubstitutionResult(subResult, header))
                {
                    request.Headers.Remove(header);
                    request.Headers.Add(header, requiredPrefix + (subResult.SecureString ?? existing));
                    mutations.AddSubstitutionHeaderKey(header);
                }
            }

            // Query parameter substitutions
            HashSet<string> subQueryParams;
            lock (_stateLock) { subQueryParams = new HashSet<string>(_substitutionQueryParams); }
            if (subQueryParams.Count > 0 && request.RequestUri != null)
            {
                string raw = request.RequestUri.Query;
                string q = raw.StartsWith("?") ? raw.Substring(1) : raw;
                var parts = q.Split('&');
                bool changed = false;
                var newParts = new List<string>();
                foreach (var part in parts)
                {
                    var kv = part.Split('=', 2);
                    if (kv.Length == 2 && subQueryParams.Contains(Uri.UnescapeDataString(kv[0])))
                    {
                        string pk = Uri.UnescapeDataString(kv[0]);
                        string pv = Uri.UnescapeDataString(kv[1]);
                        var sub = FetchSecureStringWithFailureCache(pv, null);
                        if (mutator.HandleInterceptorQueryParamSubstitutionResult(sub, pk))
                        {
                            newParts.Add($"{kv[0]}={Uri.EscapeDataString(sub.SecureString ?? pv)}");
                            mutations.AddSubstitutionQueryParamKey(pk);
                            changed = true;
                            continue;
                        }
                    }
                    newParts.Add(part);
                }
                if (changed)
                    request.RequestUri = new UriBuilder(request.RequestUri)
                        { Query = string.Join("&", newParts) }.Uri;
            }

            request = mutator.HandleInterceptorProcessedRequest(request, mutations);

            return new ApproovUpdateResponse { Request = request,
                Decision = ApproovFetchDecision.ShouldProceed,
                SdkMessage = tokenResult.LoggableToken };
        }
        catch (NetworkingErrorException ex)
        {
            Log(ApproovLogLevel.Warning, $"UpdateRequestWithApproov networking error: {ex.Message}");
            return new ApproovUpdateResponse { Request = request,
                Decision = ApproovFetchDecision.ShouldRetry, SdkMessage = ex.Message, Error = ex };
        }
        catch (RejectionException ex)
        {
            Log(ApproovLogLevel.Warning, $"UpdateRequestWithApproov rejection: {ex.Message}");
            lock (_stateLock) { _lastARC = ex.ARC; }
            return new ApproovUpdateResponse { Request = request,
                Decision = ApproovFetchDecision.ShouldFail, SdkMessage = ex.Message, Error = ex };
        }
        catch (Exception ex)
        {
            Log(ApproovLogLevel.Error, $"UpdateRequestWithApproov error: {ex.Message}");
            return new ApproovUpdateResponse { Request = request,
                Decision = ApproovFetchDecision.ShouldFail, SdkMessage = ex.Message, Error = ex };
        }
    }


    /// <summary>
    /// Decides whether to trust the server for a TLS handshake. Enforces normal certificate
    /// validation first (rejecting expired, hostname-mismatched or otherwise untrusted
    /// certificates), then applies Approov public-key pinning across the validated chain.
    /// Intended for use as an <see cref="System.Net.Http.HttpClientHandler"/>
    /// ServerCertificateCustomValidationCallback.
    /// </summary>
    public static bool VerifyServerTrust(HttpRequestMessage request,
        X509Certificate2? serverCert, X509Chain? chain, SslPolicyErrors sslPolicyErrors)
    {
        // Preserve the platform's own certificate validation: any chain, hostname or
        // name/expiry failure it reported must reject the connection before pinning.
        if (sslPolicyErrors != SslPolicyErrors.None) return false;
        if (serverCert == null || chain == null) return false;
        var chainCertificates = new List<X509Certificate2>(chain.ChainElements.Count);
        foreach (var element in chain.ChainElements)
            chainCertificates.Add(element.Certificate);
        return VerifyPinning(request, chainCertificates);
    }

    public static bool VerifyPinning(HttpRequestMessage request,
        IReadOnlyList<X509Certificate2> chainCertificates)
    {
        if (!_sdkInitialized || _isBypassMode) return true;
        IApproovServiceMutator mutator;
        lock (_stateLock) { mutator = _serviceMutator; }
        if (!mutator.HandlePinningShouldProcessRequest(request)) return true;

        string? pinsJson = PlatformGetPinsJSON("public-key-sha256");
        if (string.IsNullOrEmpty(pinsJson)) return true;

        string host = request.RequestUri?.Host ?? "";
        if (string.IsNullOrEmpty(host)) return true;

        using var pinsDoc = System.Text.Json.JsonDocument.Parse(pinsJson);
        var root = pinsDoc.RootElement;
        // Host not present in the pin set: this host is not being pinned, so accept.
        if (!root.TryGetProperty(host, out var pinArray)) return true;

        // An empty pin list for the host means "use the managed trust roots" published
        // under the "*" entry, if any.
        if (pinArray.ValueKind == System.Text.Json.JsonValueKind.Array
            && pinArray.GetArrayLength() == 0
            && root.TryGetProperty("*", out var managedRoots))
            pinArray = managedRoots;

        // Still no pins to enforce: the chain already passed certificate validation, so
        // this level of trust is acceptable and the connection is allowed.
        if (pinArray.ValueKind != System.Text.Json.JsonValueKind.Array
            || pinArray.GetArrayLength() == 0)
            return true;

        var pins = new HashSet<string>();
        foreach (var pin in pinArray.EnumerateArray())
            if (pin.GetString() is { } s) pins.Add(s);

        // Match a pin against the public key of any certificate in the validated chain
        // (leaf, intermediates or root), mirroring the other Approov service libraries.
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        foreach (var certificate in chainCertificates)
        {
            byte[]? certKeyBytes = PlatformExtractPublicKeyBytes(certificate);
            if (certKeyBytes == null) continue; // cannot pin this element; already validated
            string certPinBase64 = Convert.ToBase64String(sha256.ComputeHash(certKeyBytes));
            if (pins.Contains(certPinBase64)) return true;
        }
        return false;
    }
}

// Failure cache helpers
public static partial class ApproovService
{
    private static IApproovTokenFetchResult FetchApproovTokenWithFailureCache(string url)
    {
        ManualResetEventSlim? waitHandle;
        lock (_failureCacheLock)
        {
            if (_failureCacheResult != null && DateTime.UtcNow < _failureCacheExpiry)
                return _failureCacheResult;
            if (_failureCacheMissGroup != null) { waitHandle = _failureCacheMissGroup; }
            else { _failureCacheMissGroup = new ManualResetEventSlim(false); waitHandle = null; }
        }
        if (waitHandle != null)
        {
            waitHandle.Wait();
            lock (_failureCacheLock)
            {
                if (_failureCacheResult != null && DateTime.UtcNow < _failureCacheExpiry)
                    return _failureCacheResult;
            }
        }
        var result = PlatformFetchApproovTokenAndWait(url);
        lock (_failureCacheLock)
        {
            if (result.Status is ApproovTokenFetchStatus.NoNetwork
                or ApproovTokenFetchStatus.PoorNetwork or ApproovTokenFetchStatus.MitmDetected)
            {
                double ttl;
                lock (_stateLock) { ttl = _failureCacheTTL; }
                _failureCacheResult = result;
                _failureCacheExpiry = DateTime.UtcNow.AddSeconds(ttl);
            }
            else { _failureCacheResult = null; _failureCacheExpiry = DateTime.MinValue; }
            _failureCacheMissGroup?.Set();
            _failureCacheMissGroup = null;
        }
        return result;
    }

    private static IApproovTokenFetchResult FetchSecureStringWithFailureCache(string key, string? newDef)
    {
        lock (_failureCacheLock)
        {
            if (_failureCacheResult != null && DateTime.UtcNow < _failureCacheExpiry)
                return _failureCacheResult;
        }
        var result = PlatformFetchSecureStringAndWait(key, newDef);
        lock (_failureCacheLock)
        {
            if (result.Status is ApproovTokenFetchStatus.NoNetwork
                or ApproovTokenFetchStatus.PoorNetwork or ApproovTokenFetchStatus.MitmDetected)
            {
                double ttl;
                lock (_stateLock) { ttl = _failureCacheTTL; }
                _failureCacheResult = result;
                _failureCacheExpiry = DateTime.UtcNow.AddSeconds(ttl);
            }
        }
        return result;
    }
}
