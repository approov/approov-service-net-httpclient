// ApproovService.MAUI/util/sig/ApproovDefaultMessageSigning.cs
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Approov.Util.HttpSfv;

namespace Approov.Util.Sig;

public delegate SignatureParameters? SignatureParametersFactory(
    HttpRequestMessage request, Approov.IApproovTokenFetchResult tokenResult);

public static class ApproovDefaultMessageSigning
{
    // Called by ApproovService.SignRequest
    public static HttpRequestMessage SignRequest(
        HttpRequestMessage request,
        Approov.IApproovServiceMutator mutator,
        Approov.IApproovTokenFetchResult tokenResult)
    {
        var factory = GetSignatureParametersFactory(mutator);
        if (factory == null) return request;

        var sigParams = factory(request, tokenResult);
        if (sigParams == null) return request;

        var provider = new ApproovHttpMessageComponentProvider(request);
        string sigBase = SignatureBaseBuilder.Build(sigParams, provider);
        byte[] sigBaseBytes = Encoding.ASCII.GetBytes(sigBase);

        byte[]? keyBytes = GetSigningKey(mutator);
        if (keyBytes == null)
        {
            Approov.ApproovService.Log(Approov.ApproovLogLevel.Warning,
                "SignRequest: no signing key available (fail-open)");
            return request;
        }

        byte[] signature = SignWithES256(sigBaseBytes, keyBytes);
        string signatureLabel = GetSignatureLabel(mutator) ?? "sig1";
        string sigParamsLabel = GetSignatureParamsLabel(mutator) ?? "sig-params";

        // Serialize Signature-Input header value — must match @signature-params in the sig base
        string sigParamsValue = SignatureBaseBuilder.BuildSignatureParamsValue(sigParams);
        string sigInputValue = $"{sigParamsLabel}={sigParamsValue}";
        string signatureValue = SFV.SerializeDictionary(signatureLabel, signature);

        request.Headers.Add("Signature-Input", sigInputValue);
        request.Headers.Add("Signature", signatureValue);
        return request;
    }

    private static byte[] SignWithES256(byte[] data, byte[] pkcs8PrivateKeyBytes)
    {
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportPkcs8PrivateKey(pkcs8PrivateKeyBytes, out _);
        byte[] raw = ecdsa.SignData(data, HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return EncodeToDer(raw);
    }

    private static byte[] EncodeToDer(byte[] rawSig)
    {
        // rawSig is r||s each 32 bytes (P-256). Encode as DER SEQUENCE { INTEGER r, INTEGER s }
        int half = rawSig.Length / 2;
        byte[] r = PrepareInteger(rawSig, 0, half);
        byte[] s = PrepareInteger(rawSig, half, half);
        int seqLen = 2 + r.Length + 2 + s.Length;
        var der = new byte[2 + seqLen];
        int pos = 0;
        der[pos++] = 0x30; der[pos++] = (byte)seqLen;
        der[pos++] = 0x02; der[pos++] = (byte)r.Length;
        r.CopyTo(der, pos); pos += r.Length;
        der[pos++] = 0x02; der[pos++] = (byte)s.Length;
        s.CopyTo(der, pos);
        return der;
    }

    private static byte[] PrepareInteger(byte[] src, int offset, int length)
    {
        // Strip leading zeros; prepend 0x00 if high bit set
        int start = offset;
        while (start < offset + length - 1 && src[start] == 0) start++;
        bool needsPad = (src[start] & 0x80) != 0;
        int count = offset + length - start;
        var result = new byte[needsPad ? count + 1 : count];
        if (needsPad) result[0] = 0x00;
        Array.Copy(src, start, result, needsPad ? 1 : 0, count);
        return result;
    }

    private static SignatureParametersFactory? GetSignatureParametersFactory(
        Approov.IApproovServiceMutator mutator)
    {
        if (mutator is IApproovMessageSigner signer)
            return signer.GetSignatureParametersFactory();
        return null;
    }

    private static byte[]? GetSigningKey(Approov.IApproovServiceMutator mutator)
    {
        if (mutator is IApproovMessageSigner signer) return signer.GetSigningKey();
        return null;
    }

    private static string? GetSignatureLabel(Approov.IApproovServiceMutator mutator)
    {
        if (mutator is IApproovMessageSigner signer) return signer.SignatureLabel;
        return "sig1";
    }

    private static string? GetSignatureParamsLabel(Approov.IApproovServiceMutator mutator)
    {
        if (mutator is IApproovMessageSigner signer) return signer.SignatureParamsLabel;
        return "sig-params";
    }
}

// Optional interface implemented by mutators that want HTTP message signing
public interface IApproovMessageSigner
{
    SignatureParametersFactory? GetSignatureParametersFactory();
    byte[]? GetSigningKey();
    string SignatureLabel { get; }
    string SignatureParamsLabel { get; }
}
