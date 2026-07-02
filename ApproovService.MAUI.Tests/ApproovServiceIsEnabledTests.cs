// ApproovService.MAUI.Tests/ApproovServiceIsEnabledTests.cs
using Xunit;

namespace Approov.Tests;

[Collection("ApproovService")]
public class ApproovServiceIsEnabledTests : IDisposable
{
    public void Dispose() => ApproovService.ResetForTesting();

    [Fact]
    public void IsApproovEnabled_NotInitialized_ReturnsFalse()
    {
        Assert.False(ApproovService.IsApproovEnabled());
    }

    [Fact]
    public void IsApproovEnabled_BypassMode_ReturnsFalse()
    {
        ApproovService.Initialize("");
        Assert.True(ApproovService.IsInitialized());
        Assert.False(ApproovService.IsApproovEnabled());
    }

    [Fact]
    public void IsApproovEnabled_RealConfig_ReturnsTrue()
    {
        ApproovService.Initialize("dummy-config");
        Assert.True(ApproovService.IsApproovEnabled());
    }

    [Fact]
    public void IsApproovEnabled_AfterReset_ReturnsFalse()
    {
        ApproovService.Initialize("dummy-config");
        ApproovService.ResetForTesting();
        Assert.False(ApproovService.IsApproovEnabled());
    }
}
