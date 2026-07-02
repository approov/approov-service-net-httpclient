// ApproovService.MAUI.Tests/util/SignatureBaseBuilderTests.cs
using System.Net.Http;
using Approov.Util.HttpSfv;
using Approov.Util.Sig;
using Xunit;

namespace Approov.Tests;

public class SignatureBaseBuilderTests
{
    [Fact]
    public void Build_MethodAndPath_ProducesExpectedBase()
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "https://example.com/api/v1");
        var sp = new SignatureParameters();
        sp.AddComponentIdentifier(new StringItem("@method"));
        sp.AddComponentIdentifier(new StringItem("@path"));
        sp.AddParameter("created", 1735000000L);

        var provider = new ApproovHttpMessageComponentProvider(req);
        string sigBase = SignatureBaseBuilder.Build(sp, provider);

        Assert.Contains("\"@method\": GET", sigBase);
        Assert.Contains("\"@path\": /api/v1", sigBase);
        Assert.Contains("\"@signature-params\":", sigBase);
    }

    [Fact]
    public void Build_SignatureParamsLine_IsLastLine()
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "https://example.com/submit");
        var sp = new SignatureParameters();
        sp.AddComponentIdentifier(new StringItem("@method"));
        sp.AddParameter("created", 1735000000L);

        var provider = new ApproovHttpMessageComponentProvider(req);
        string sigBase = SignatureBaseBuilder.Build(sp, provider);

        var lines = sigBase.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.StartsWith("\"@signature-params\":", lines[^1]);
    }
}
