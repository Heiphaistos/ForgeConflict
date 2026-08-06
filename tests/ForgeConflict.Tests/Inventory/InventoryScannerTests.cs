using ForgeConflict.Core.Collection;
using ForgeConflict.Core.Inventory;

namespace ForgeConflict.Tests.Inventory;

public sealed class InventoryScannerTests
{
    [Fact]
    public async Task ScanAsync_ProducesAFragmentForEverySource()
    {
        var scanner = new InventoryScanner(new IsolatedRunner(new TestLog()));

        var snapshot = await scanner.ScanAsync();

        Assert.NotNull(snapshot.Gpus);
        Assert.NotNull(snapshot.DriverPackages);
        Assert.NotNull(snapshot.Services);
        Assert.NotNull(snapshot.GraphicsSettings);
        Assert.NotNull(snapshot.OverlayProcesses);
        Assert.NotNull(snapshot.GpuEvents);
        Assert.NotNull(snapshot.CrashArtifacts);
        Assert.NotNull(snapshot.PcieLinks);
        Assert.NotNull(snapshot.Power);
        Assert.NotNull(snapshot.Displays);
        Assert.Equal(10, snapshot.AllFragments.Count);
    }

    [Fact]
    public async Task ScanAsync_RecordsMachineContext()
    {
        var scanner = new InventoryScanner(new IsolatedRunner(new TestLog()));

        var snapshot = await scanner.ScanAsync();

        Assert.False(string.IsNullOrWhiteSpace(snapshot.MachineName));
        Assert.False(string.IsNullOrWhiteSpace(snapshot.OsVersion));
        Assert.True(snapshot.CapturedAt <= DateTimeOffset.Now);
    }

    [Fact]
    public async Task ScanAsync_ReportsFailedSourcesWithoutAbortingTheScan()
    {
        var scanner = new InventoryScanner(new IsolatedRunner(new TestLog()));

        var snapshot = await scanner.ScanAsync();

        // Certaines sources peuvent echouer selon la machine : ce qui compte est
        // qu'un echec soit visible et n'empeche pas les autres d'aboutir.
        Assert.All(snapshot.AllFragments, f => Assert.False(string.IsNullOrWhiteSpace(f.Source)));
        Assert.Contains(snapshot.AllFragments, f => f.Status == FragmentStatus.Ok);
    }

    [Fact]
    public async Task ScanAsync_ExposesUnusableSourcesForDisplay()
    {
        var scanner = new InventoryScanner(new IsolatedRunner(new TestLog()));

        var snapshot = await scanner.ScanAsync();

        // L'interface doit pouvoir dire ce qui manque plutot que de presenter un
        // diagnostic partiel comme s'il etait complet.
        Assert.Equal(
            snapshot.AllFragments.Count(f => !f.IsUsable),
            snapshot.UnusableSources.Count);
    }

    [Fact]
    public async Task ScanAsync_RunsSourcesConcurrently()
    {
        var scanner = new InventoryScanner(new IsolatedRunner(new TestLog()));
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        await scanner.ScanAsync();
        stopwatch.Stop();

        // La somme des delais depasse deux minutes. Une execution sequentielle qui
        // atteindrait ses limites serait bien plus lente que cette borne.
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(60),
            $"le scan a pris {stopwatch.Elapsed}, les sources ne semblent pas paralleles");
    }
}
