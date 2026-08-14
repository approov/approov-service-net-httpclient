// ApproovService.MAUI.Tests/SnapshotTokenFetchResultTests.cs
// Regression test for the .NET Android token-fetch-status marshalling bug reported by Moz
// ("Unknown approov token fetch result SUCCESS"). On .NET Android the SDK TokenFetchStatus
// binds as a Java.Lang.Enum whose managed peer is marshalled across JNI on every read, so a
// live result's Status can be observed inconsistently across reads. The service layer now
// snapshots the native result once at the fetch boundary, so every downstream consumer sees
// one stable value. These tests reproduce the inconsistency and prove the snapshot removes it.
using System.Collections.Generic;
using Xunit;

namespace Approov.Tests;

public class SnapshotTokenFetchResultTests
{
    // Models a fetch result whose Status differs between reads (the failure mode behind the
    // Moz report) and counts how many times Status is read.
    private sealed class FlakyTokenFetchResult : IApproovTokenFetchResult
    {
        private readonly Queue<ApproovTokenFetchStatus> _statuses;
        public int StatusReads { get; private set; }

        public FlakyTokenFetchResult(params ApproovTokenFetchStatus[] sequence)
            => _statuses = new Queue<ApproovTokenFetchStatus>(sequence);

        public ApproovTokenFetchStatus Status
        {
            get
            {
                StatusReads++;
                // Return each queued value once, then keep returning the last one.
                return _statuses.Count > 1 ? _statuses.Dequeue() : _statuses.Peek();
            }
        }

        public string Token => "";
        public string? SecureString => null;
        public string ARC => "";
        public string RejectionReasons => "";
        public bool IsConfigChanged => false;
        public bool IsForceApplyPins => false;
        public string LoggableToken => "";
        public string? TraceID => null;
    }

    [Fact]
    public void FlakySource_IsInconsistentAcrossReads_DemonstratesTheBug()
    {
        var flaky = new FlakyTokenFetchResult(
            ApproovTokenFetchStatus.InternalError, ApproovTokenFetchStatus.Success);

        // Two reads of the raw result disagree — exactly the condition that produced
        // "Unknown approov token fetch result SUCCESS": the decision saw a non-success
        // status while a later read/log saw SUCCESS.
        Assert.Equal(ApproovTokenFetchStatus.InternalError, flaky.Status);
        Assert.Equal(ApproovTokenFetchStatus.Success, flaky.Status);
    }

    [Fact]
    public void Snapshot_ReadsSourceStatusOnce_AndIsStable()
    {
        var flaky = new FlakyTokenFetchResult(
            ApproovTokenFetchStatus.InternalError, ApproovTokenFetchStatus.Success);

        var snapshot = new SnapshotTokenFetchResult(flaky);

        // The source Status was read exactly once, at the boundary...
        Assert.Equal(1, flaky.StatusReads);
        // ...and every consumer now sees the same stable value (that single boundary read).
        Assert.Equal(ApproovTokenFetchStatus.InternalError, snapshot.Status);
        Assert.Equal(snapshot.Status, snapshot.Status);
        Assert.Equal(1, flaky.StatusReads); // reading the snapshot never touches the source
    }

    [Fact]
    public void Of_DoesNotReWrapAnExistingSnapshot()
    {
        var snap = new SnapshotTokenFetchResult(
            new StubTokenFetchResult { Status = ApproovTokenFetchStatus.Success });
        Assert.Same(snap, SnapshotTokenFetchResult.Of(snap));
    }
}
