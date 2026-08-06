using System.Diagnostics;
using ForgeConflict.Core.Collection;
using ForgeConflict.Core.Logging;

namespace ForgeConflict.Tests.Collection;

public sealed class IsolatedRunnerTests
{
    [Fact]
    public async Task RunAsync_ReturnsOkWhenWorkSucceeds()
    {
        var runner = new IsolatedRunner(new TestLog());

        var fragment = await runner.RunAsync("gpu", _ => 7, TimeSpan.FromSeconds(5));

        Assert.Equal(FragmentStatus.Ok, fragment.Status);
        Assert.Equal(7, fragment.Value);
    }

    [Fact]
    public async Task RunAsync_CapturesExceptionInsteadOfPropagating()
    {
        var log = new TestLog();
        var runner = new IsolatedRunner(log);

        var fragment = await runner.RunAsync<int>(
            "registre",
            _ => throw new UnauthorizedAccessException("acces refuse"),
            TimeSpan.FromSeconds(5));

        Assert.Equal(FragmentStatus.Failed, fragment.Status);
        Assert.Contains("acces refuse", fragment.Error!, StringComparison.Ordinal);
        Assert.True(log.Contains(LogLevel.Error, "registre"));
    }

    [Fact]
    public async Task RunAsync_ReturnsTimedOutWithoutWaitingForBlockedWork()
    {
        var log = new TestLog();
        var runner = new IsolatedRunner(log);
        using var release = new ManualResetEventSlim(false);
        var stopwatch = Stopwatch.StartNew();

        var fragment = await runner.RunAsync<int>(
            "wmi",
            _ => { release.Wait(TimeSpan.FromSeconds(30)); return 1; },
            TimeSpan.FromMilliseconds(200));

        stopwatch.Stop();
        release.Set();

        Assert.Equal(FragmentStatus.TimedOut, fragment.Status);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5),
            $"l'appel a rendu la main en {stopwatch.Elapsed}, il ne doit pas attendre le travail bloque");
        Assert.True(log.Contains(LogLevel.Warn, "wmi"));
    }

    [Fact]
    public async Task RunAsync_IsolatesFailuresBetweenConcurrentCollectors()
    {
        var runner = new IsolatedRunner(new TestLog());

        var results = await Task.WhenAll(
            runner.RunAsync<int>("a", _ => throw new InvalidOperationException("boum"), TimeSpan.FromSeconds(5)),
            runner.RunAsync("b", _ => 2, TimeSpan.FromSeconds(5)),
            runner.RunAsync("c", _ => 3, TimeSpan.FromSeconds(5)));

        Assert.Equal(FragmentStatus.Failed, results[0].Status);
        Assert.Equal(FragmentStatus.Ok, results[1].Status);
        Assert.Equal(FragmentStatus.Ok, results[2].Status);
    }
}
