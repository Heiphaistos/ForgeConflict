using ForgeConflict.Core.Collection;
using ForgeConflict.Core.Inventory.Collectors;
using ForgeConflict.Core.Inventory.Model;

namespace ForgeConflict.Core.Inventory;

/// <summary>
/// Execute toutes les sources d'inventaire en parallele et agrege le resultat.
/// Chaque source passe par l'executeur isole : ni une exception ni un blocage ne
/// peuvent interrompre le scan.
/// </summary>
public sealed class InventoryScanner
{
    private readonly IIsolatedRunner _runner;
    private readonly GpuCollector _gpu = new();
    private readonly DriverStoreCollector _driverStore = new();
    private readonly ServiceCollector _services = new();
    private readonly GraphicsSettingsCollector _settings = new();
    private readonly OverlayProcessCollector _overlays = new();
    private readonly GpuEventCollector _events = new();
    private readonly CrashArtifactCollector _crashes = new();
    private readonly PcieCollector _pcie = new();
    private readonly PowerCollector _power = new();
    private readonly DisplayCollector _displays = new();

    public InventoryScanner(IIsolatedRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
    }

    public async Task<SystemSnapshot> ScanAsync(CancellationToken cancellationToken = default)
    {
        var gpus = Run<IReadOnlyList<GpuInfo>>(_gpu, cancellationToken);
        var packages = Run<IReadOnlyList<DriverPackage>>(_driverStore, cancellationToken);
        var services = Run<IReadOnlyList<ServiceInfo>>(_services, cancellationToken);
        var settings = Run<GraphicsSettings>(_settings, cancellationToken);
        var overlays = Run<IReadOnlyList<ProcessInfo>>(_overlays, cancellationToken);
        var events = Run<IReadOnlyList<GpuEvent>>(_events, cancellationToken);
        var crashes = Run<IReadOnlyList<CrashArtifact>>(_crashes, cancellationToken);
        var pcie = Run<IReadOnlyList<KeyValuePair<string, PcieLink>>>(_pcie, cancellationToken);
        var power = Run<PowerProfile>(_power, cancellationToken);
        var displays = Run<IReadOnlyList<DisplayMode>>(_displays, cancellationToken);

        await Task.WhenAll(
            gpus, packages, services, settings, overlays,
            events, crashes, pcie, power, displays).ConfigureAwait(false);

        return new SystemSnapshot(
            CapturedAt: DateTimeOffset.Now,
            MachineName: Environment.MachineName,
            OsVersion: Environment.OSVersion.VersionString,
            Gpus: gpus.Result,
            DriverPackages: packages.Result,
            Services: services.Result,
            GraphicsSettings: settings.Result,
            OverlayProcesses: overlays.Result,
            GpuEvents: events.Result,
            CrashArtifacts: crashes.Result,
            PcieLinks: pcie.Result,
            Power: power.Result,
            Displays: displays.Result);
    }

    private Task<Fragment<T>> Run<T>(ICollector<T> collector, CancellationToken cancellationToken) =>
        _runner.RunAsync(collector.Source, collector.Collect, collector.Timeout, cancellationToken);
}
