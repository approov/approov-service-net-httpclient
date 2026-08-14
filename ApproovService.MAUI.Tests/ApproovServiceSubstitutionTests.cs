// ApproovService.MAUI.Tests/ApproovServiceSubstitutionTests.cs
using Xunit;

namespace Approov.Tests;

[Collection("ApproovService")]
public class ApproovServiceSubstitutionTests : IDisposable
{
    public void Dispose() => ApproovService.ResetForTesting();

    [Fact]
    public void AddSubstitutionHeader_AppearsInGetters()
    {
        ApproovService.Initialize("");
        ApproovService.AddSubstitutionHeader("X-Secret", null);
        Assert.True(ApproovService.GetSubstitutionHeaders().ContainsKey("X-Secret"));
    }

    [Fact]
    public void RemoveSubstitutionHeader_RemovesEntry()
    {
        ApproovService.Initialize("");
        ApproovService.AddSubstitutionHeader("X-Secret", null);
        ApproovService.RemoveSubstitutionHeader("X-Secret");
        Assert.False(ApproovService.GetSubstitutionHeaders().ContainsKey("X-Secret"));
    }

    [Fact]
    public void AddSubstitutionQueryParam_AppearsInGetters()
    {
        ApproovService.Initialize("");
        ApproovService.AddSubstitutionQueryParam("api_key");
        Assert.Contains("api_key", ApproovService.GetSubstitutionQueryParams());
    }

    [Fact]
    public void RemoveSubstitutionQueryParam_RemovesEntry()
    {
        ApproovService.Initialize("");
        ApproovService.AddSubstitutionQueryParam("api_key");
        ApproovService.RemoveSubstitutionQueryParam("api_key");
        Assert.DoesNotContain("api_key", ApproovService.GetSubstitutionQueryParams());
    }

    [Fact]
    public void AddExclusionURLRegex_MatchesURL()
    {
        ApproovService.Initialize("");
        ApproovService.AddExclusionURLRegex("internal", @"https://internal\.example\.com/.*");
        var regexs = ApproovService.GetExclusionURLRegexs();
        Assert.Matches(regexs["internal"], "https://internal.example.com/api/v1");
    }

    [Fact]
    public void RemoveExclusionURLRegex_RemovesEntry()
    {
        ApproovService.Initialize("");
        ApproovService.AddExclusionURLRegex("internal", @"https://internal\.example\.com/.*");
        ApproovService.RemoveExclusionURLRegex("internal");
        Assert.False(ApproovService.GetExclusionURLRegexs().ContainsKey("internal"));
    }

    [Fact]
    public void AddExclusionURLRegex_CommonOverload_UsesPatternAsRemovalKey()
    {
        const string pattern = @"https://internal\.example\.com/.*";
        ApproovService.Initialize("");
        ApproovService.AddExclusionURLRegex(pattern);

        Assert.True(ApproovService.GetExclusionURLRegexs().ContainsKey(pattern));

        ApproovService.RemoveExclusionURLRegex(pattern);
        Assert.False(ApproovService.GetExclusionURLRegexs().ContainsKey(pattern));
    }
}
