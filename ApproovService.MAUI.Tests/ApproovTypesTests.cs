using Xunit;

namespace Approov.Tests;

public class ApproovTypesTests
{
    [Fact]
    public void NetworkingErrorException_ShouldRetry_IsTrue()
    {
        var ex = new NetworkingErrorException("network failed");
        Assert.True(ex.ShouldRetry);
    }

    [Fact]
    public void PermanentException_ShouldRetry_IsFalse()
    {
        var ex = new PermanentException("permanent");
        Assert.False(ex.ShouldRetry);
    }

    [Fact]
    public void RejectionException_CarriesArcAndReasons()
    {
        var ex = new RejectionException("rejected", arc: "ARC123", rejectionReasons: "debug");
        Assert.Equal("ARC123", ex.ARC);
        Assert.Equal("debug", ex.RejectionReasons);
        Assert.False(ex.ShouldRetry);
    }

    [Fact]
    public void ApproovUpdateResponse_DefaultDecision_IsNotSet()
    {
        var r = new ApproovUpdateResponse();
        Assert.Equal(default(ApproovFetchDecision), r.Decision);
        Assert.Null(r.Request);
    }

    [Fact]
    public void ApproovTokenFetchStatus_HasExpectedValues()
    {
        Assert.Equal(0, (int)ApproovTokenFetchStatus.Success);
        Assert.Equal(1, (int)ApproovTokenFetchStatus.NoNetwork);
        Assert.Equal(6, (int)ApproovTokenFetchStatus.Rejected);
    }
}
