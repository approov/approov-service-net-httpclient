// ApproovService.MAUI/util/sig/ApproovDefaultMessageSigning.cs
//
// HTTP message signing mirroring approov-service-retrofit's ApproovDefaultMessageSigning.
// It is registered as the Approov service mutator; it adds RFC 9421 Signature /
// Signature-Input headers to any request that already carries an Approov token.
//
// Two signing modes are supported, selected per request by the SignatureParametersFactory:
//   * install signing  -> alg "ecdsa-p256-sha256", signature id "install".
//                         The SDK returns a base64 ASN.1 DER ES256 signature which is
//                         decoded to the raw R||S (64 byte) form required by RFC 9421 §3.3.4.
//   * account signing  -> alg "hmac-sha256", signature id "account". The SDK returns the
//                         base64 HMAC which is used directly.
using System;
using System.Collections.Generic;
using System.Formats.Asn1;
using System.Net.Http;
using System.Numerics;
using Approov.Util.HttpSfv;

namespace Approov.Util.Sig;

public class ApproovDefaultMessageSigning : IApproovServiceMutator
{
    public const string DIGEST_SHA256 = "sha-256";
    public const string DIGEST_SHA512 = "sha-512";
    public const string ALG_ES256 = "ecdsa-p256-sha256"; // install (device) private key
    public const string ALG_HS256 = "hmac-sha256";       // account signing key

    private SignatureParametersFactory? _defaultFactory;
    private readonly Dictionary<string, SignatureParametersFactory> _hostFactories = new();

    public ApproovDefaultMessageSigning SetDefaultFactory(SignatureParametersFactory factory)
    {
        _defaultFactory = factory;
        return this;
    }

    public ApproovDefaultMessageSigning PutHostFactory(string hostName, SignatureParametersFactory factory)
    {
        _hostFactories[hostName] = factory;
        return this;
    }

    private SignatureParametersFactory? SelectFactory(string host)
        => _hostFactories.TryGetValue(host, out var f) ? f : _defaultFactory;

    // ---- IApproovServiceMutator: delegate everything except the signing step ----
    private static IApproovServiceMutator Base => ApproovServiceMutatorDefault.Shared;
    public void HandlePrecheckResult(IApproovTokenFetchResult r) => Base.HandlePrecheckResult(r);
    public void HandleFetchTokenResult(IApproovTokenFetchResult r) => Base.HandleFetchTokenResult(r);
    public void HandleFetchSecureStringResult(IApproovTokenFetchResult r, string op, string key)
        => Base.HandleFetchSecureStringResult(r, op, key);
    public void HandleFetchCustomJWTResult(IApproovTokenFetchResult r) => Base.HandleFetchCustomJWTResult(r);
    public bool HandleInterceptorShouldProcessRequest(HttpRequestMessage req)
        => Base.HandleInterceptorShouldProcessRequest(req);
    public bool HandleInterceptorFetchTokenResult(IApproovTokenFetchResult r, string url)
        => Base.HandleInterceptorFetchTokenResult(r, url);
    public bool HandleInterceptorHeaderSubstitutionResult(IApproovTokenFetchResult r, string h)
        => Base.HandleInterceptorHeaderSubstitutionResult(r, h);
    public bool HandleInterceptorQueryParamSubstitutionResult(IApproovTokenFetchResult r, string k)
        => Base.HandleInterceptorQueryParamSubstitutionResult(r, k);
    public bool HandlePinningShouldProcessRequest(HttpRequestMessage req)
        => Base.HandlePinningShouldProcessRequest(req);

    /// <summary>
    /// Adds message signature headers to a request that has passed through the Approov
    /// interceptor. Only requests that carry an Approov token and that have a configured
    /// factory are signed.
    /// </summary>
    public HttpRequestMessage HandleInterceptorProcessedRequest(
        HttpRequestMessage request, ApproovRequestMutations changes)
    {
        if (changes?.TokenHeaderKey == null)
            return request; // no Approov token was added, so nothing to sign

        var factory = SelectFactory(request.RequestUri?.Host ?? "");
        if (factory == null)
            return request;

        SignatureParameters sigParams = factory.BuildSignatureParameters(request, changes);

        var provider = new ApproovHttpMessageComponentProvider(request);
        string message = SignatureBaseBuilder.Build(sigParams, provider);
        // WARNING: never log `message` in production - it contains the Approov token.

        string alg = sigParams.GetParameterValue("alg") as string
            ?? throw new InvalidOperationException("Signature parameters are missing the alg parameter");

        string sigId;
        byte[] signature;
        switch (alg)
        {
            case ALG_ES256:
            {
                sigId = "install";
                string? base64 = Approov.ApproovService.GetInstallMessageSignature(message);
                if (string.IsNullOrEmpty(base64))
                {
                    Approov.ApproovService.Log(Approov.ApproovLogLevel.Debug,
                        "message signing: no install signature available - skipping");
                    return request; // fail-open
                }
                // The SDK returns base64 ASN.1 DER; RFC 9421 §3.3.4 requires raw R||S.
                signature = DerEcdsaToRaw(Convert.FromBase64String(base64));
                break;
            }
            case ALG_HS256:
            {
                sigId = "account";
                string? base64 = Approov.ApproovService.GetAccountMessageSignature(message);
                if (string.IsNullOrEmpty(base64))
                {
                    Approov.ApproovService.Log(Approov.ApproovLogLevel.Debug,
                        "message signing: no account signature available - skipping");
                    return request; // fail-open
                }
                signature = Convert.FromBase64String(base64);
                break;
            }
            default:
                throw new InvalidOperationException("Unsupported algorithm identifier: " + alg);
        }

        string sigHeader = SFV.SerializeDictionary(sigId, signature);
        string sigInputHeader = $"{sigId}={SignatureBaseBuilder.BuildSignatureParamsValue(sigParams)}";

        request.Headers.Remove("Signature");
        request.Headers.Remove("Signature-Input");
        request.Headers.TryAddWithoutValidation("Signature-Input", sigInputHeader);
        request.Headers.TryAddWithoutValidation("Signature", sigHeader);
        return request;
    }

    /// <summary>
    /// Converts an ASN.1 DER encoded ECDSA P-256 signature to the raw R||S (64 byte)
    /// concatenation required by RFC 9421 §3.3.4 for the "ecdsa-p256-sha256" algorithm.
    /// </summary>
    internal static byte[] DerEcdsaToRaw(byte[] der)
    {
        var reader = new AsnReader(der, AsnEncodingRules.DER);
        AsnReader seq = reader.ReadSequence();
        reader.ThrowIfNotEmpty(); // reject trailing data after the outer SEQUENCE
        BigInteger r = seq.ReadInteger();
        BigInteger s = seq.ReadInteger();
        seq.ThrowIfNotEmpty();
        var raw = new byte[64];
        WriteUnsigned32(r, raw, 0);
        WriteUnsigned32(s, raw, 32);
        return raw;
    }

    private static void WriteUnsigned32(BigInteger value, byte[] dest, int offset)
    {
        byte[] be = value.ToByteArray(isUnsigned: true, isBigEndian: true);
        if (be.Length > 32)
            throw new InvalidOperationException("ECDSA signature integer exceeds 32 bytes");
        Array.Copy(be, 0, dest, offset + (32 - be.Length), be.Length);
    }

    /// <summary>
    /// The default factory mirrors approov-service-retrofit: install signing over
    /// @method, @target-uri and the Approov token header, with created and a 15 second
    /// expiry, plus optional Authorization / Content-Length / Content-Type and a body digest.
    /// </summary>
    public static SignatureParametersFactory GenerateDefaultSignatureParametersFactory()
    {
        const long defaultExpiresLifetime = 15; // seconds - covers retry time and clock skew
        var baseParameters = new SignatureParameters();
        baseParameters.AddComponentIdentifier(new StringItem("@method"));
        baseParameters.AddComponentIdentifier(new StringItem("@target-uri"));
        return new SignatureParametersFactory()
            .SetBaseParameters(baseParameters)
            .SetUseInstallMessageSigning()
            .SetAddCreated(true)
            .SetExpiresLifetime(defaultExpiresLifetime)
            .SetAddApproovTokenHeader(true)
            .SetAddApproovTraceIDHeader(true)
            .AddOptionalHeaders("Authorization", "Content-Length", "Content-Type")
            .SetBodyDigestConfig(DIGEST_SHA256, false);
    }

    /// <summary>
    /// Builds a per-request <see cref="SignatureParameters"/> from configurable settings.
    /// </summary>
    public class SignatureParametersFactory
    {
        private SignatureParameters? _baseParameters;
        private string? _bodyDigestAlgorithm;
        private bool _bodyDigestRequired;
        private bool _useAccountMessageSigning;
        private bool _addCreated;
        private long _expiresLifetime;
        private bool _addApproovTokenHeader;
        private bool _addApproovTraceIDHeader;
        private readonly List<string> _optionalHeaders = new();

        // Test seam so created/expires are deterministic in unit tests.
        internal Func<long> NowSeconds { get; set; } = () => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        public SignatureParametersFactory SetBaseParameters(SignatureParameters baseParameters)
        {
            _baseParameters = baseParameters;
            return this;
        }

        public SignatureParametersFactory SetBodyDigestConfig(string? bodyDigestAlgorithm, bool required)
        {
            if (bodyDigestAlgorithm == null)
                required = false;
            else if (bodyDigestAlgorithm != DIGEST_SHA256 && bodyDigestAlgorithm != DIGEST_SHA512)
                throw new ArgumentException("Unsupported body digest algorithm: " + bodyDigestAlgorithm);
            _bodyDigestAlgorithm = bodyDigestAlgorithm;
            _bodyDigestRequired = required;
            return this;
        }

        public SignatureParametersFactory SetUseInstallMessageSigning() { _useAccountMessageSigning = false; return this; }
        public SignatureParametersFactory SetUseAccountMessageSigning() { _useAccountMessageSigning = true; return this; }
        public SignatureParametersFactory SetAddCreated(bool addCreated) { _addCreated = addCreated; return this; }
        public SignatureParametersFactory SetExpiresLifetime(long seconds) { _expiresLifetime = seconds; return this; }
        public SignatureParametersFactory SetAddApproovTokenHeader(bool v) { _addApproovTokenHeader = v; return this; }
        public SignatureParametersFactory SetAddApproovTraceIDHeader(bool v) { _addApproovTraceIDHeader = v; return this; }

        public SignatureParametersFactory AddOptionalHeaders(params string[] headers)
        {
            _optionalHeaders.AddRange(headers);
            return this;
        }

        public SignatureParameters BuildSignatureParameters(HttpRequestMessage request, ApproovRequestMutations changes)
        {
            var p = new SignatureParameters();

            // base components (and any base parameters) first
            if (_baseParameters != null)
            {
                foreach (var component in _baseParameters.ToComponentValue())
                    p.AddComponentIdentifier(component);
                foreach (var (key, value) in _baseParameters.GetParameters())
                    p.AddParameter(key, value);
            }

            // algorithm - inserted before created/expires to match the reference ordering
            p.AddParameter("alg", _useAccountMessageSigning ? ALG_HS256 : ALG_ES256);

            if (_addCreated || _expiresLifetime > 0)
            {
                long now = NowSeconds();
                if (_addCreated) p.AddParameter("created", now);
                if (_expiresLifetime > 0) p.AddParameter("expires", now + _expiresLifetime);
            }

            if (_addApproovTokenHeader && changes.TokenHeaderKey != null)
                p.AddComponentIdentifier(new StringItem(changes.TokenHeaderKey.ToLowerInvariant()));

            if (_addApproovTraceIDHeader && changes.TraceIDHeaderKey != null)
                p.AddComponentIdentifier(new StringItem(changes.TraceIDHeaderKey.ToLowerInvariant()));

            foreach (var header in _optionalHeaders)
                if (HasHeader(request, header))
                    p.AddComponentIdentifier(new StringItem(header.ToLowerInvariant()));

            // The Content-Digest header itself is produced by the body-digest step of the
            // message handler; here we only cover it as a component when it is present.
            if (_bodyDigestAlgorithm != null)
            {
                if (HasHeader(request, "Content-Digest"))
                    p.AddComponentIdentifier(new StringItem("content-digest"));
                else if (_bodyDigestRequired)
                    throw new InvalidOperationException("Required Content-Digest header is missing");
            }

            return p;
        }

        private static bool HasHeader(HttpRequestMessage request, string name)
        {
            // Content-Length / Content-Type live on HttpContent; calling Contains for them
            // on the request-header collection throws "Misused header name", so guard both.
            if (TryContains(request.Headers, name)) return true;
            var content = request.Content;
            return content != null && TryContains(content.Headers, name);
        }

        private static bool TryContains(System.Net.Http.Headers.HttpHeaders headers, string name)
        {
            try { return headers.Contains(name); }
            catch (InvalidOperationException) { return false; }
        }
    }
}
