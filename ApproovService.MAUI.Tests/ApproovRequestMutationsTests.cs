using Xunit;

namespace Approov.Tests;

public class ApproovRequestMutationsTests
{
    [Fact]
    public void DefaultMutations_AllNullOrEmpty()
    {
        var m = new ApproovRequestMutations();
        Assert.Null(m.TokenHeaderKey);
        Assert.Null(m.TraceIDHeaderKey);
        Assert.Null(m.OriginalURL);
        Assert.Empty(m.SubstitutionHeaderKeys);
        Assert.Empty(m.SubstitutionQueryParamKeys);
    }

    [Fact]
    public void AddSubstitutionHeaderKey_AppearsInList()
    {
        var m = new ApproovRequestMutations();
        m.AddSubstitutionHeaderKey("X-My-Secret");
        Assert.Single(m.SubstitutionHeaderKeys);
        Assert.Equal("X-My-Secret", m.SubstitutionHeaderKeys[0]);
    }

    [Fact]
    public void AddSubstitutionQueryParamKey_AppearsInList()
    {
        var m = new ApproovRequestMutations();
        m.AddSubstitutionQueryParamKey("api_key");
        Assert.Single(m.SubstitutionQueryParamKeys);
        Assert.Equal("api_key", m.SubstitutionQueryParamKeys[0]);
    }

    [Fact]
    public void SetProperties_RoundTrip()
    {
        var m = new ApproovRequestMutations
        {
            TokenHeaderKey = "Approov-Token",
            TraceIDHeaderKey = "Approov-TraceID",
            OriginalURL = "https://example.com/api"
        };
        Assert.Equal("Approov-Token", m.TokenHeaderKey);
        Assert.Equal("Approov-TraceID", m.TraceIDHeaderKey);
        Assert.Equal("https://example.com/api", m.OriginalURL);
    }
}
