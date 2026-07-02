using System.Net.Http;
using Approov.Util.Sig;
using Xunit;

namespace Approov.Tests;

public class ComponentProviderTests
{
    private static HttpRequestMessage MakeRequest(string url, HttpMethod? method = null)
        => new HttpRequestMessage(method ?? HttpMethod.Get, url);

    [Fact]
    public void GetComponentValue_Method_ReturnsUpperCaseMethod()
    {
        var req = MakeRequest("https://example.com/path");
        var provider = new ApproovHttpMessageComponentProvider(req);
        Assert.Equal("GET", provider.GetComponentValue("@method"));
    }

    [Fact]
    public void GetComponentValue_Path_ReturnsPath()
    {
        var req = MakeRequest("https://example.com/api/v1/data");
        var provider = new ApproovHttpMessageComponentProvider(req);
        Assert.Equal("/api/v1/data", provider.GetComponentValue("@path"));
    }

    [Fact]
    public void GetComponentValue_Authority_ReturnsHost()
    {
        var req = MakeRequest("https://example.com/path");
        var provider = new ApproovHttpMessageComponentProvider(req);
        Assert.Equal("example.com", provider.GetComponentValue("@authority"));
    }

    [Fact]
    public void GetComponentValue_Query_ReturnsQueryWithoutLeadingMark()
    {
        var req = MakeRequest("https://example.com/path?foo=bar&baz=qux");
        var provider = new ApproovHttpMessageComponentProvider(req);
        // Swift URL.query strips the leading '?'; C# Uri.Query retains it
        Assert.Equal("foo=bar&baz=qux", provider.GetComponentValue("@query"));
    }

    [Fact]
    public void GetComponentValue_RequestTarget_ReturnsCombined()
    {
        var req = MakeRequest("https://example.com/path?foo=bar");
        var provider = new ApproovHttpMessageComponentProvider(req);
        Assert.Equal("/path?foo=bar", provider.GetComponentValue("@request-target"));
    }

    [Fact]
    public void GetComponentValue_ArbitraryHeader_ReturnsHeaderValue()
    {
        var req = MakeRequest("https://example.com/path");
        req.Headers.Add("X-Custom-Header", "custom-value");
        var provider = new ApproovHttpMessageComponentProvider(req);
        Assert.Equal("custom-value", provider.GetComponentValue("x-custom-header"));
    }

    [Fact]
    public void GetComponentValue_UnknownHeader_ThrowsInvalidOperation()
    {
        var req = MakeRequest("https://example.com/path");
        var provider = new ApproovHttpMessageComponentProvider(req);
        Assert.Throws<InvalidOperationException>(
            () => provider.GetComponentValue("x-nonexistent-header"));
    }
}
