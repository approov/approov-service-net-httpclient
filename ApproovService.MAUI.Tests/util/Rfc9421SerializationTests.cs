// ApproovService.MAUI.Tests/util/Rfc9421SerializationTests.cs
// Compliance tests pinning our serialization to the RFCs' own worked examples:
//  - RFC 8941 (Structured Field Values) §4.1.8 Byte Sequence
//  - RFC 9421 (HTTP Message Signatures) §2.5 signature base, §2.2.2 @target-uri
using System.Net.Http;
using System.Text;
using Approov.Util.HttpSfv;
using Approov.Util.Sig;
using Xunit;

namespace Approov.Tests;

public class Rfc9421SerializationTests
{
    [Fact]
    public void ByteSequence_SerializesPerRfc8941Section418()
    {
        // RFC 8941 §4.1.8 worked example: the Byte Sequence for the ASCII bytes of
        // "pretend this is binary content." serializes to :<base64>: with standard
        // base64 (RFC 4648) including '=' padding.
        byte[] bytes = Encoding.ASCII.GetBytes("pretend this is binary content.");
        Assert.Equal(
            "b=:cHJldGVuZCB0aGlzIGlzIGJpbmFyeSBjb250ZW50Lg==:",
            SFV.SerializeDictionary("b", bytes));
    }

    [Fact]
    public void SignatureBase_MatchesRfc9421Section25Example()
    {
        // Exact signature base from RFC 9421 §2.5 (line-wrap backslashes in the RFC
        // text removed; each covered component on its own line, LF-separated, and the
        // final "@signature-params" line carries the inner list + parameters with no
        // trailing newline).
        var sp = new SignatureParameters();
        foreach (var c in new[]
        {
            "@method", "@authority", "@path",
            "content-digest", "content-length", "content-type"
        })
        {
            sp.AddComponentIdentifier(new StringItem(c));
        }
        sp.AddParameter("created", 1618884473L);          // Integer: unquoted
        sp.AddParameter("keyid", "test-key-rsa-pss");      // String: quoted

        string actual = SignatureBaseBuilder.Build(sp, new RfcExampleProvider());

        string expected =
            "\"@method\": POST\n" +
            "\"@authority\": example.com\n" +
            "\"@path\": /foo\n" +
            "\"content-digest\": sha-512=:WZDPaVn/7XgHaAy8pmojAkGWoRx2UFChF41A2svX+TaPm+AbwAgBWnrIiYllu7BNNyealdVLvRwEmTHWXvJwew==:\n" +
            "\"content-length\": 18\n" +
            "\"content-type\": application/json\n" +
            "\"@signature-params\": (\"@method\" \"@authority\" \"@path\" \"content-digest\" \"content-length\" \"content-type\");created=1618884473;keyid=\"test-key-rsa-pss\"";

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void SignatureParamsValue_MatchesBaseTrailer()
    {
        // The Signature-Input header value MUST be byte-for-byte identical to the
        // @signature-params trailer used in the signature base (RFC 9421 §2.3/§2.5),
        // otherwise the verifier reconstructs a different base.
        var sp = new SignatureParameters();
        sp.AddComponentIdentifier(new StringItem("@method"));
        sp.AddParameter("created", 1618884473L);
        sp.AddParameter("keyid", "test-key-rsa-pss");

        Assert.Equal(
            "(\"@method\");created=1618884473;keyid=\"test-key-rsa-pss\"",
            SignatureBaseBuilder.BuildSignatureParamsValue(sp));
    }

    [Fact]
    public void TargetUri_DerivedComponent_ReturnsFullAbsoluteUri()
    {
        // RFC 9421 §2.2.2: @target-uri is the full absolute target URI of the request.
        var req = new HttpRequestMessage(HttpMethod.Get,
            "https://www.example.com/path?param=value");
        var provider = new ApproovHttpMessageComponentProvider(req);

        Assert.Equal("https://www.example.com/path?param=value",
            provider.GetComponentValue("@target-uri"));
    }

    private sealed class RfcExampleProvider : IComponentProvider
    {
        public string GetComponentValue(string identifier) => identifier switch
        {
            "@method" => "POST",
            "@authority" => "example.com",
            "@path" => "/foo",
            "content-digest" =>
                "sha-512=:WZDPaVn/7XgHaAy8pmojAkGWoRx2UFChF41A2svX+TaPm+AbwAgBWnrIiYllu7BNNyealdVLvRwEmTHWXvJwew==:",
            "content-length" => "18",
            "content-type" => "application/json",
            _ => throw new System.InvalidOperationException(identifier)
        };
    }
}
