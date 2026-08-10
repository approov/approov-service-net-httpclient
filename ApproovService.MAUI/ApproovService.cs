// ApproovService.MAUI/ApproovService.cs
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using Approov.Util.Sig;

namespace Approov;

public static partial class ApproovService
{
    // Locks
    private static readonly object _initLock = new();
    private static readonly object _stateLock = new();
    private static readonly object _loggingLock = new();
    private static readonly object _failureCacheLock = new();
    private static readonly object _bindingFetchLock = new();

    // Initialization state. These flags are written under _initLock but read lock-free on
    // hot paths (EnsureInitialized, UpdateRequestWithApproov, VerifyPinning); volatile gives
    // those reads acquire semantics so they observe a completed Initialize. _isBypassMode is
    // written before _sdkInitialized, so a reader that sees _sdkInitialized also sees it.
    private static volatile bool _sdkInitialized = false;
    private static string? _configUsed = null;
    private static volatile bool _isBypassMode = false;

    // Token configuration
    private static string _approovTokenHeader = "Approov-Token";
    private static string _approovTokenPrefix = "";
    private static string? _approovTraceIDHeader = "Approov-TraceID";
    private static string? _bindingHeader = null;
    private static bool _useApproovStatusIfNoToken = false;
    private static bool _bodyDigestEnabled = true;
    private static bool _bodyDigestRequired = false;

    // Service wiring
    private static IApproovServiceMutator _serviceMutator = CreateInitialServiceMutator();
    private static bool _isInitialServiceMutator = true;

    // Substitution tables
    private static Dictionary<string, string> _substitutionHeaders = new();
    private static HashSet<string> _substitutionQueryParams = new();
    private static Dictionary<string, Regex> _exclusionURLRegexs = new();

    // Audit / logging
    private static string _lastARC = "";
    private static ApproovLogLevel _loggingLevel = ApproovLogLevel.Info;

    // Failure cache
    private static IApproovTokenFetchResult? _failureCacheResult = null;
    private static string? _failureCacheKey = null;
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
    // Writes to the platform log (logcat on Android, the device console on iOS). Must not
    // be compiled out of release builds.
    private static partial void PlatformLog(ApproovLogLevel level, string message);

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

    private static IApproovServiceMutator CreateInitialServiceMutator()
        => new ApproovDefaultMessageSigning().SetDefaultFactory(CreateDefaultSigningFactory());

    private static ApproovDefaultMessageSigning.SignatureParametersFactory
        CreateDefaultSigningFactory()
    {
        var factory = ApproovDefaultMessageSigning.GenerateDefaultSignatureParametersFactory();
        return factory.SetBodyDigestConfig(
            _bodyDigestEnabled ? ApproovDefaultMessageSigning.DIGEST_SHA256 : null,
            _bodyDigestRequired);
    }

    // Resets configuration that belongs to the request service layer, including any custom
    // service mutator. Every successful initialization is a boundary: an app that installs a
    // mutator must reinstall it afterwards. This matches the React Native service layer,
    // which resets both on Android and iOS and records the mutator reset as security
    // relevant, and it is the reason it matters here too: a custom mutator participates in
    // rejection handling and substitution decisions, so it must not outlive the
    // initialization it was scoped to.
    private static void ResetRuntimeConfiguration()
    {
        if (_bindingHeader != null)
            Log(ApproovLogLevel.Warning,
                "initialization is discarding the binding header");
        if (!_isInitialServiceMutator)
            Log(ApproovLogLevel.Warning,
                "initialization is discarding a custom service mutator");

        _approovTokenHeader = "Approov-Token";
        _approovTokenPrefix = "";
        _approovTraceIDHeader = "Approov-TraceID";
        _bindingHeader = null;
        _useApproovStatusIfNoToken = false;
        _bodyDigestEnabled = true;
        _bodyDigestRequired = false;
        _substitutionHeaders = new();
        _substitutionQueryParams = new();
        _exclusionURLRegexs = new();
        _serviceMutator = CreateInitialServiceMutator();
        _isInitialServiceMutator = true;
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
                lock (_stateLock) { ResetRuntimeConfiguration(); }
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
            PlatformSetUserProperty("approov-service-maui/3.5.5");

            // Same-config re-initialization is an initialization boundary too, so runtime
            // configuration and any custom mutator are reset here as well, and only after
            // native success. Matches the React Native service layer, which resets on every
            // initialize regardless of whether the configuration changed.
            lock (_stateLock) { ResetRuntimeConfiguration(); }
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
        // Dispatched to a platform sink rather than System.Diagnostics.Debug.WriteLine,
        // which is [Conditional("DEBUG")] and so was erased from every release build: the
        // shipped package logged nothing at all and SetLoggingLevel had no effect. Every
        // fail-open path in this layer reports through here, so losing it in release meant
        // losing all visibility of requests that proceeded without Approov protection.
        if (level <= current)
            PlatformLog(level, message);
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

    internal static bool GetUseApproovStatusIfNoToken()
    {
        lock (_stateLock) { return _useApproovStatusIfNoToken; }
    }

    public static void SetBodyDigestEnabled(bool enabled)
    {
        SetBodyDigestEnabled(enabled, false);
    }

    // Configures Content-Digest generation. When required is true (and the digest is
    // enabled) a POST/PUT/PATCH request whose body cannot be digested (one-shot
    // streaming content, an empty body, or a digest computation failure) fails closed
    // instead of being sent without a Content-Digest header. This mirrors the native
    // React Native signers: "required" means a digest must actually be generated.
    // required is only meaningful while enabled; each call fully specifies the config,
    // so SetBodyDigestEnabled(enabled) clears any previously set required mode.
    public static void SetBodyDigestEnabled(bool enabled, bool required)
    {
        lock (_stateLock)
        {
            _bodyDigestEnabled = enabled;
            _bodyDigestRequired = enabled && required;
            if (_isInitialServiceMutator
                && _serviceMutator is ApproovDefaultMessageSigning signer)
                signer.SetDefaultFactory(CreateDefaultSigningFactory());
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
        lock (_stateLock)
        {
            if (mutator == null)
            {
                _serviceMutator = CreateInitialServiceMutator();
                _isInitialServiceMutator = true;
            }
            else
            {
                _serviceMutator = mutator;
                _isInitialServiceMutator = false;
            }
        }
    }

    public static IApproovServiceMutator GetServiceMutator()
    {
        lock (_stateLock) { return _serviceMutator; }
    }

    public static void SetFailureCacheTTL(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0)
            throw new ArgumentOutOfRangeException(nameof(seconds),
                "Failure cache TTL must be a finite, non-negative number of seconds");
        lock (_stateLock) { _failureCacheTTL = seconds; }
    }

    public static Dictionary<string, Regex> GetExclusionURLRegexs()
    {
        lock (_stateLock) { return new Dictionary<string, Regex>(_exclusionURLRegexs); }
    }

#if APPROOV_TESTING
    // Resets every piece of service-layer state, including _sdkInitialized. Both TLS entry
    // points (VerifyPinning, VerifyPinsForHost) accept any certificate while uninitialized,
    // so this method disables token injection and certificate pinning process-wide in a
    // single call. It must never be reachable in a shipped build, in any configuration:
    // APPROOV_TESTING is defined only by ApproovService.MAUI.Tests.csproj. Do not relax this
    // to #if DEBUG, because the unit suite is also run in Release.
    internal static void ResetForTesting()
    {
        lock (_initLock) lock (_stateLock) lock (_loggingLock) lock (_failureCacheLock)
        {
            _sdkInitialized = false; _configUsed = null; _isBypassMode = false;
            _approovTokenHeader = "Approov-Token"; _approovTokenPrefix = "";
            _approovTraceIDHeader = "Approov-TraceID"; _bindingHeader = null;
            _useApproovStatusIfNoToken = false; _bodyDigestEnabled = true;
            _bodyDigestRequired = false;
            _serviceMutator = CreateInitialServiceMutator();
            _isInitialServiceMutator = true;
            _substitutionHeaders = new(); _substitutionQueryParams = new();
            _exclusionURLRegexs = new(); _lastARC = "";
            _loggingLevel = ApproovLogLevel.Info;
            _failureCacheResult = null; _failureCacheKey = null;
            _failureCacheExpiry = DateTime.MinValue;
            _failureCacheTTL = 5.0; _failureCacheMissGroup = null;
        }
    }
#endif
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

    // Common service-layer API. The pattern is also a stable removal key; retain the
    // named overload above for source compatibility with existing MAUI consumers.
    public static void AddExclusionURLRegex(string pattern)
        => AddExclusionURLRegex(pattern, pattern);

    public static void RemoveExclusionURLRegex(string name)
    {
        lock (_stateLock) { _exclusionURLRegexs.Remove(name); }
    }

    // === SDK operations ===

    public static void Precheck()
    {
        EnsureInitialized();
        if (_isBypassMode)
            throw new PermanentException("precheck: Approov is disabled");
        // A secure-string lookup performs an attestation without requiring a
        // protected API hostname. UNKNOWN_KEY is the expected successful outcome.
        var result = PlatformFetchSecureStringAndWait("precheck-dummy-key", null);
        IApproovServiceMutator mutator;
        lock (_stateLock) { mutator = _serviceMutator; }
        mutator.HandlePrecheckResult(result);
    }

    public static IApproovTokenFetchResult FetchApproovToken(string url)
    {
        EnsureInitialized();
        if (_isBypassMode) return new BypassFetchResult(ApproovTokenFetchStatus.UnknownUrl);
        IApproovTokenFetchResult result;
        // Serialize direct token fetches with interceptor binding updates so they cannot
        // observe or disturb the SDK data hash halfway through a bound request.
        lock (_bindingFetchLock)
            result = PlatformFetchApproovTokenAndWait(url);
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
        if (!_isBypassMode)
        {
            // The SDK data hash is global persistent state. Use the same lock as bound
            // request processing and direct token fetches to prevent mid-fetch changes.
            lock (_bindingFetchLock) PlatformSetDataHashInToken(data);
        }
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
    internal static readonly HttpRequestOptionsKey<bool> SkipSubstitutionsOption =
        new("Approov.SkipSubstitutions");

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

            // Binding data is persistent global SDK state. Validate the configured header
            // and keep setting its value atomic with the associated token fetch so that
            // concurrent requests cannot receive tokens bound to each other's data.
            string? bindingHeader;
            lock (_stateLock) { bindingHeader = _bindingHeader; }
            IApproovTokenFetchResult tokenResult;
            if (bindingHeader != null)
            {
                // Binding is persistent SDK state, so every token fetch must take this
                // lock while binding is configured. Otherwise a bound request could
                // change the SDK hash while a concurrent request without the header is
                // already fetching a token. Header presence remains optional, matching
                // React Native; when present, serialize all values as the transport does.
                lock (_bindingFetchLock)
                {
                    if (request.Headers.TryGetValues(bindingHeader, out var bindingValues))
                        PlatformSetDataHashInToken(string.Join(",", bindingValues));
                    tokenResult = FetchApproovTokenWithFailureCache(url);
                }
            }
            else
            {
                tokenResult = FetchApproovTokenWithFailureCache(url);
            }

            if (tokenResult.IsConfigChanged)
            {
                PlatformFetchConfig();
                Log(ApproovLogLevel.Info, "Dynamic Approov configuration update received");
            }
            if (tokenResult.IsForceApplyPins)
            {
                // Reading the pins applies/clears the SDK force flag. Abort this
                // request afterwards so no pooled connection can bypass the refresh.
                string? refreshedPins = PlatformGetPinsJSON("public-key-sha256");
                if (string.IsNullOrEmpty(refreshedPins))
                    throw new PinningErrorException(
                        "Approov requested a pin refresh but returned no pins");
                throw new NetworkingErrorException("Approov pins need to be updated");
            }

            string tokenHeader, tokenPrefix;
            lock (_stateLock) { tokenHeader = _approovTokenHeader; tokenPrefix = _approovTokenPrefix; }

            bool useStatus;
            lock (_stateLock) { useStatus = _useApproovStatusIfNoToken; }

            bool shouldContinue = mutator.HandleInterceptorFetchTokenResult(tokenResult, url);
            if (!shouldContinue)
            {
                // UNKNOWN_URL, UNPROTECTED_URL and (by default) NO_APPROOV_SERVICE
                // must be forwarded unchanged. In particular, do not resolve secure
                // strings into a domain that is unknown to Approov pinning.
                return new ApproovUpdateResponse { Request = request,
                    Decision = ApproovFetchDecision.ShouldProceed,
                    SdkMessage = StatusToString(tokenResult.Status) };
            }

            bool addedTokenHeader = false;
            if (tokenResult.Status == ApproovTokenFetchStatus.Success
                && !string.IsNullOrEmpty(tokenResult.Token))
            {
                request.Headers.Remove(tokenHeader);
                request.Headers.Add(tokenHeader, tokenPrefix + tokenResult.Token);
                addedTokenHeader = true;
            }
            else if (useStatus && string.IsNullOrEmpty(tokenResult.Token))
            {
                request.Headers.Remove(tokenHeader);
                request.Headers.Add(tokenHeader, tokenPrefix + StatusToString(tokenResult.Status));
                addedTokenHeader = true;
            }

            // TraceID header
            string? traceIDHeader;
            lock (_stateLock) { traceIDHeader = _approovTraceIDHeader; }
            if (traceIDHeader != null && !string.IsNullOrEmpty(tokenResult.TraceID))
            {
                request.Headers.Remove(traceIDHeader);
                request.Headers.Add(traceIDHeader, tokenResult.TraceID);
            }

            // Header substitutions
            var mutations = new ApproovRequestMutations
            {
                // A proceeding status value is token-header material too and is
                // included in message signing by the React Native implementation.
                TokenHeaderKey = addedTokenHeader ? tokenHeader : null,
                TraceIDHeaderKey = traceIDHeader != null
                    && !string.IsNullOrEmpty(tokenResult.TraceID) ? traceIDHeader : null,
                OriginalURL = url
            };

            bool skipSubstitutions = request.Options.TryGetValue(
                SkipSubstitutionsOption, out bool skip) && skip;
            Dictionary<string, string> subHeaders;
            lock (_stateLock)
            {
                subHeaders = skipSubstitutions
                    ? new Dictionary<string, string>()
                    : new Dictionary<string, string>(_substitutionHeaders);
            }
            foreach (var (header, requiredPrefix) in subHeaders)
            {
                if (!request.Headers.TryGetValues(header, out var existingValues)) continue;
                string existing = string.Join(",", existingValues);
                if (existing.Length <= requiredPrefix.Length) continue;
                if (!string.IsNullOrEmpty(requiredPrefix) && !existing.StartsWith(requiredPrefix)) continue;
                string lookupKey = string.IsNullOrEmpty(requiredPrefix)
                    ? existing : existing.Substring(requiredPrefix.Length);
                var subResult = FetchSecureStringWithFailureCache(lookupKey, null);
                if (mutator.HandleInterceptorHeaderSubstitutionResult(subResult, header)
                    && subResult.Status == ApproovTokenFetchStatus.Success
                    && subResult.SecureString != null)
                {
                    request.Headers.Remove(header);
                    request.Headers.Add(header, requiredPrefix + (subResult.SecureString ?? existing));
                    mutations.AddSubstitutionHeaderKey(header);
                }
            }

            // Query parameter substitutions
            HashSet<string> subQueryParams;
            lock (_stateLock)
            {
                subQueryParams = skipSubstitutions
                    ? new HashSet<string>()
                    : new HashSet<string>(_substitutionQueryParams);
            }
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
                        if (mutator.HandleInterceptorQueryParamSubstitutionResult(sub, pk)
                            && sub.Status == ApproovTokenFetchStatus.Success
                            && sub.SecureString != null)
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

    internal static string StatusToString(ApproovTokenFetchStatus status) => status switch
    {
        ApproovTokenFetchStatus.Success => "SUCCESS",
        ApproovTokenFetchStatus.NoNetwork => "NO_NETWORK",
        ApproovTokenFetchStatus.MitmDetected => "MITM_DETECTED",
        ApproovTokenFetchStatus.PoorNetwork => "POOR_NETWORK",
        ApproovTokenFetchStatus.Disabled => "DISABLED",
        ApproovTokenFetchStatus.UnknownKey => "UNKNOWN_KEY",
        ApproovTokenFetchStatus.Rejected => "REJECTED",
        ApproovTokenFetchStatus.UnknownUrl => "UNKNOWN_URL",
        ApproovTokenFetchStatus.UnprotectedUrl => "UNPROTECTED_URL",
        ApproovTokenFetchStatus.NoApproovService => "NO_APPROOV_SERVICE",
        ApproovTokenFetchStatus.BadPayload => "BAD_PAYLOAD",
        _ => "INTERNAL_ERROR"
    };


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
        return VerifyPinning(request, CollectChainCertificates(serverCert, chain));
    }

    /// <summary>
    /// Trust callback for <see cref="System.Net.Http.SocketsHttpHandler"/>, whose
    /// SslOptions.RemoteCertificateValidationCallback receives the <see cref="SslStream"/>
    /// rather than the request. The SNI target host is all that is needed, because pinning
    /// is keyed by host.
    /// </summary>
    internal static bool VerifyServerTrustForStream(object? sender,
        X509Certificate? certificate, X509Chain? chain, SslPolicyErrors sslPolicyErrors)
    {
        if (sslPolicyErrors != SslPolicyErrors.None) return false;
        if (certificate is not X509Certificate2 serverCert || chain == null) return false;
        string host = (sender as SslStream)?.TargetHostName ?? "";
        return VerifyPinsForHost(host, CollectChainCertificates(serverCert, chain));
    }

    private static List<X509Certificate2> CollectChainCertificates(
        X509Certificate2 serverCert, X509Chain chain)
    {
        var chainCertificates = new List<X509Certificate2>(
            Math.Max(1, chain.ChainElements.Count + chain.ChainPolicy.ExtraStore.Count));
        foreach (var element in chain.ChainElements)
            AddCertificateIfMissing(chainCertificates, element.Certificate);

        // Some Android callbacks expose only the leaf (or no ChainElements) while the
        // peer intermediates remain in ExtraStore. Merge both sources unconditionally so
        // an intermediate/root pin is not lost merely because a leaf element was present.
        AddCertificateIfMissing(chainCertificates, serverCert);
        foreach (var certificate in chain.ChainPolicy.ExtraStore)
            AddCertificateIfMissing(chainCertificates, certificate);
        return chainCertificates;
    }

    private static void AddCertificateIfMissing(
        List<X509Certificate2> certificates, X509Certificate2 candidate)
    {
        if (!certificates.Any(existing =>
                existing.RawData.AsSpan().SequenceEqual(candidate.RawData)))
            certificates.Add(candidate);
    }

    public static bool VerifyPinning(HttpRequestMessage request,
        IReadOnlyList<X509Certificate2> chainCertificates)
    {
        if (!_sdkInitialized || _isBypassMode) return true;

        // The service mutator is deliberately NOT consulted here. This method's return value
        // is the TLS trust decision itself, so honouring a "skip pinning" answer would mean
        // accepting the certificate outright, not falling back to some other check. That
        // made application code able to switch off the one control ordinary certificate
        // validation cannot provide. The native iOS trust callback never consulted it (it
        // carries no HttpRequestMessage) and the React Native layer declares the hook but
        // never calls it on either platform, so removing it also aligns all four ports.

        // IdnHost, not Host: for an internationalized domain Host yields the Unicode form
        // while Approov pin sets, the token fetch URL and the same-origin check all use
        // punycode. Using Host made the pin lookup miss and fall through to "not pinned".
        return VerifyPinsForHost(request.RequestUri?.IdnHost ?? "", chainCertificates);
    }

    // Native iOS trust callbacks do not provide the original HttpRequestMessage. Apply
    // pins directly to the authentication challenge host instead of fabricating a GET
    // that could cause a request-aware custom mutator to skip pinning incorrectly.
    internal static bool VerifyPinsForHost(string host,
        IReadOnlyList<X509Certificate2> chainCertificates)
    {
        if (!_sdkInitialized || _isBypassMode) return true;

        string? pinsJson = PlatformGetPinsJSON("public-key-sha256");
        // Once the service is initialized, the SDK should always return a JSON pin set.
        // A null/empty result indicates an SDK/state failure; accepting it would silently
        // disable Approov pinning for every host.
        if (string.IsNullOrEmpty(pinsJson)) return false;

        // No resolvable host means no pin lookup is possible, so there is nothing to
        // enforce against. Fail closed: the native iOS path rejects this condition before
        // reaching here, and so does the reference implementation.
        if (string.IsNullOrEmpty(host)) return false;

        using var pinsDoc = System.Text.Json.JsonDocument.Parse(pinsJson);
        var root = pinsDoc.RootElement;
        // Host not present in the pin set: this host is not being pinned, so accept.
        if (!TryGetPinsForHost(root, host, out var pinArray)) return true;

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

    private static bool TryGetPinsForHost(System.Text.Json.JsonElement root,
        string host, out System.Text.Json.JsonElement pins)
    {
        foreach (var property in root.EnumerateObject())
        {
            string pattern = property.Name;
            if (pattern == "*") continue; // managed-root fallback, not a hostname
            if (string.Equals(pattern, host, StringComparison.OrdinalIgnoreCase)
                || HostMatchesWildcard(host, pattern))
            {
                pins = property.Value;
                return true;
            }
        }
        pins = default;
        return false;
    }

    private static bool HostMatchesWildcard(string host, string pattern)
    {
        if (pattern.StartsWith("**.", StringComparison.Ordinal))
        {
            string suffix = pattern[3..];
            return string.Equals(host, suffix, StringComparison.OrdinalIgnoreCase)
                || host.EndsWith("." + suffix, StringComparison.OrdinalIgnoreCase);
        }
        if (!pattern.StartsWith("*.", StringComparison.Ordinal)) return false;
        string oneLabelSuffix = pattern[2..];
        string suffixWithDot = "." + oneLabelSuffix;
        if (!host.EndsWith(suffixWithDot, StringComparison.OrdinalIgnoreCase)) return false;
        string prefix = host[..^suffixWithDot.Length];
        return prefix.Length > 0 && !prefix.Contains('.');
    }
}

// Failure cache helpers
public static partial class ApproovService
{
    private static IApproovTokenFetchResult FetchApproovTokenWithFailureCache(string url)
    {
        string cacheKey = "token\0" + url;
        ManualResetEventSlim? waitHandle;
        bool isLeader;
        lock (_failureCacheLock)
        {
            if (_failureCacheKey == cacheKey && _failureCacheResult != null
                && DateTime.UtcNow < _failureCacheExpiry)
                return _failureCacheResult;
            if (_failureCacheMissGroup != null) { waitHandle = _failureCacheMissGroup; isLeader = false; }
            else { _failureCacheMissGroup = new ManualResetEventSlim(false); waitHandle = null; isLeader = true; }
        }
        if (waitHandle != null)
        {
            waitHandle.Wait();
            lock (_failureCacheLock)
            {
                if (_failureCacheKey == cacheKey && _failureCacheResult != null
                    && DateTime.UtcNow < _failureCacheExpiry)
                    return _failureCacheResult;
            }
            // The leader's result was not cacheable; fetch our own without owning the
            // miss-group (a fresh leader will coalesce any subsequent callers).
            return PlatformFetchApproovTokenAndWait(url);
        }
        try
        {
            var result = PlatformFetchApproovTokenAndWait(url);
            lock (_failureCacheLock)
            {
                if (result.Status is ApproovTokenFetchStatus.NoNetwork
                    or ApproovTokenFetchStatus.PoorNetwork or ApproovTokenFetchStatus.MitmDetected)
                {
                    double ttl;
                    lock (_stateLock) { ttl = _failureCacheTTL; }
                    _failureCacheResult = result;
                    _failureCacheKey = cacheKey;
                    _failureCacheExpiry = DateTime.UtcNow.AddSeconds(ttl);
                }
                else if (_failureCacheKey == cacheKey)
                {
                    _failureCacheResult = null; _failureCacheKey = null;
                    _failureCacheExpiry = DateTime.MinValue;
                }
            }
            return result;
        }
        finally
        {
            // The leader always releases and clears the miss-group, even if the platform
            // fetch throws, so coalesced waiters are never stranded.
            if (isLeader)
                lock (_failureCacheLock)
                {
                    _failureCacheMissGroup?.Set();
                    _failureCacheMissGroup = null;
                }
        }
    }

    private static IApproovTokenFetchResult FetchSecureStringWithFailureCache(string key, string? newDef)
    {
        string cacheKey = "secure\0" + key + "\0" + (newDef ?? "");
        lock (_failureCacheLock)
        {
            if (_failureCacheKey == cacheKey && _failureCacheResult != null
                && DateTime.UtcNow < _failureCacheExpiry)
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
                _failureCacheKey = cacheKey;
                _failureCacheExpiry = DateTime.UtcNow.AddSeconds(ttl);
            }
        }
        return result;
    }
}
