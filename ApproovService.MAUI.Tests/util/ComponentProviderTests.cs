using System.Net.Http;
using Approov.Util.Sig;
using Xunit;

namespace Approov.Tests;

public class ComponentProviderTests
{
    private static HttpRequestMessage MakeRequest(string url, HttpMethod? method = null)
        => new HttpRequestMessage(method ?? HttpMethod.Get, url);

    [Fact]
    public void GetComponentValue_Method_ReturnsMethodValue()
    {
        var req = MakeRequest("https://example.com/path");
        var provider = new ApproovHttpMessageComponentProvider(req);
        Assert.Equal("GET", provider.GetComponentValue("@method"));
    }

    [Fact]
    public void GetComponentValue_Method_PreservesCustomMethodCase()
    {
        // RFC 9421 §2.2.1 performs no case transformation; HTTP methods are case-sensitive.
        var req = MakeRequest("https://example.com/path", new HttpMethod("CustomVerb"));
        var provider = new ApproovHttpMessageComponentProvider(req);
        Assert.Equal("CustomVerb", provider.GetComponentValue("@method"));
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
    public void GetComponentValue_Query_RetainsLeadingQuestionMark()
    {
        var req = MakeRequest("https://example.com/path?foo=bar&baz=qux");
        var provider = new ApproovHttpMessageComponentProvider(req);
        // RFC 9421 §2.2.7 includes the leading '?' in the @query value.
        Assert.Equal("?foo=bar&baz=qux", provider.GetComponentValue("@query"));
    }

    [Fact]
    public void GetComponentValue_Query_AbsentQuery_ReturnsQuestionMark()
    {
        var req = MakeRequest("https://example.com/path");
        var provider = new ApproovHttpMessageComponentProvider(req);
        // RFC 9421 §2.2.7 represents an absent query as a single '?'.
        Assert.Equal("?", provider.GetComponentValue("@query"));
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
    public void GetComponentValue_ContentType_FallsBackToContentHeaders()
    {
        var req = MakeRequest("https://example.com/path");
        req.Content = new StringContent("body", System.Text.Encoding.UTF8, "application/json");
        var provider = new ApproovHttpMessageComponentProvider(req);

        Assert.Equal("application/json; charset=utf-8",
            provider.GetComponentValue("content-type"));
    }

    [Fact]
    public void GetComponentValue_ContentLength_FallsBackToContentHeaders()
    {
        var req = MakeRequest("https://example.com/path");
        req.Content = new ByteArrayContent(new byte[] { 1, 2, 3 });
        req.Content.Headers.ContentLength = 3;
        var provider = new ApproovHttpMessageComponentProvider(req);

        Assert.Equal("3", provider.GetComponentValue("content-length"));
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
