// ApproovService.MAUI.Tests/ApproovServiceInitializationEdgeCaseTests.cs
using Xunit;

namespace Approov.Tests;

[Collection("ApproovService")]
public class ApproovServiceInitializationEdgeCaseTests : IDisposable
{
    public void Dispose()
    {
        ApproovService.InitCallCount = 0;
        ApproovService.NextInitShouldThrow = false;
        ApproovService.LastUserProperty = null;
        ApproovService.ResetForTesting();
    }

    [Fact]
    public void Initialize_EmptyThenValid_UpgradesSuccessfully()
    {
        ApproovService.Initialize("");
        Assert.True(ApproovService.IsInitialized());
        Assert.False(ApproovService.IsApproovEnabled());

        ApproovService.Initialize("real-config");
        Assert.True(ApproovService.IsInitialized());
        Assert.True(ApproovService.IsApproovEnabled());
    }

    [Fact]
    public void Initialize_SameConfigWithReinitComment_CallsSdkAgain()
    {
        ApproovService.Initialize("dummy-config");
        int callsBefore = ApproovService.InitCallCount;
        ApproovService.Initialize("dummy-config", "reinit-test");
        Assert.Equal(callsBefore + 1, ApproovService.InitCallCount);
    }

    [Fact]
    public void Initialize_SameConfigWithOptionsComment_DoesNotCallSdkAgain()
    {
        ApproovService.Initialize("dummy-config");
        int callsBefore = ApproovService.InitCallCount;
        ApproovService.Initialize("dummy-config", "options:fast");
        Assert.Equal(callsBefore, ApproovService.InitCallCount);
    }

    [Fact]
    public void Initialize_DifferentConfigAfterReal_ThrowsAndPreservesState()
    {
        ApproovService.Initialize("config-a");
        Assert.Throws<InitializationFailureException>(() =>
            ApproovService.Initialize("config-b"));
        Assert.True(ApproovService.IsApproovEnabled());
    }

    [Fact]
    public void Initialize_BypassUpgradeFails_PreservesInitializedBypassState()
    {
        ApproovService.Initialize("");
        Assert.True(ApproovService.IsInitialized());
        Assert.False(ApproovService.IsApproovEnabled());

        ApproovService.NextInitShouldThrow = true;
        Assert.Throws<Exception>(() => ApproovService.Initialize("real-config"));

        // Must remain initialized-but-disabled
        Assert.True(ApproovService.IsInitialized());
        Assert.False(ApproovService.IsApproovEnabled());
    }
}
