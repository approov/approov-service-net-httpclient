// ApproovService.MAUI.Tests/ApproovServiceVersionTelemetryTests.cs
using System.IO;
using Xunit;

namespace Approov.Tests;

[Collection("ApproovService")]
public class ApproovServiceVersionTelemetryTests : IDisposable
{
    public ApproovServiceVersionTelemetryTests()
    {
        // Reset shared state before each test in case a prior test class left it dirty
        ApproovService.LastUserProperty = null;
        ApproovService.InitCallCount = 0;
        ApproovService.ResetForTesting();
    }

    public void Dispose()
    {
        ApproovService.LastUserProperty = null;
        ApproovService.InitCallCount = 0;
        ApproovService.ResetForTesting();
    }

    [Fact]
    public void Initialize_RealConfig_SetsUserPropertyWithVersion()
    {
        ApproovService.Initialize("dummy-config");
        Assert.Equal("approov-service-maui/3.5.11", ApproovService.LastUserProperty);
    }

    [Fact]
    public void Initialize_BypassMode_DoesNotSetUserProperty()
    {
        ApproovService.Initialize("");
        Assert.Null(ApproovService.LastUserProperty);
    }

    [Fact]
    public void VersionString_MatchesChangelog()
    {
        // Walk up from the test bin directory to find CHANGELOG.md at the repo root
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "CHANGELOG.md")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        string changelog = File.ReadAllText(Path.Combine(dir!.FullName, "CHANGELOG.md"));
        Assert.Contains("[3.5.11]", changelog);
    }
}
