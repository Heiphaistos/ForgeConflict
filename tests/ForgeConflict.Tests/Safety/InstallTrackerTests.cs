using ForgeConflict.Core.Safety;

namespace ForgeConflict.Tests.Safety;

/// <summary>Le compteur est global : ces tests ne tournent pas en parallele des autres.</summary>
[CollectionDefinition(nameof(InstallTrackerTests), DisableParallelization = true)]
public sealed class InstallTrackerCollection;

[Collection(nameof(InstallTrackerTests))]
public sealed class InstallTrackerTests
{
    [Fact]
    public void CountsNestedOperations_AndSignalsIdleOnce()
    {
        var idle = 0;
        void OnIdle() => Interlocked.Increment(ref idle);
        InstallTracker.Idle += OnIdle;
        try
        {
            Assert.False(InstallTracker.IsBusy);
            var a = InstallTracker.Begin("A");
            var b = InstallTracker.Begin("B");
            Assert.Equal("A et B", InstallTracker.CurrentName);
            a.Dispose();
            a.Dispose();
            Assert.Equal("B", InstallTracker.CurrentName);
            Assert.Equal(0, idle);
            b.Dispose();
            Assert.Null(InstallTracker.CurrentName);
            Assert.Equal(1, idle);
        }
        finally
        {
            InstallTracker.Idle -= OnIdle;
        }
    }

    [Fact]
    public void ReleasesOnException_AndAcrossThreads()
    {
        static void Fail()
        {
            using var operation = InstallTracker.Begin("X");
            throw new InvalidOperationException();
        }

        Assert.Throws<InvalidOperationException>(Fail);
        Parallel.For(0, 200, _ => { using var operation = InstallTracker.Begin("P"); });
        Assert.False(InstallTracker.IsBusy);
    }
}
