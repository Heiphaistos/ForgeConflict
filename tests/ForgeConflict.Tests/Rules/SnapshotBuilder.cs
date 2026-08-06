using ForgeConflict.Core.Collection;
using ForgeConflict.Core.Inventory;
using ForgeConflict.Core.Inventory.Model;

namespace ForgeConflict.Tests.Rules;

/// <summary>
/// Fabrique des instantanes synthetiques. Une machine saine ne presente aucun des
/// cas que les regles doivent detecter : sans ce constructeur, les regles ne
/// seraient testables que sur une machine deja defaillante.
/// </summary>
internal sealed class SnapshotBuilder
{
    private readonly List<GpuInfo> _gpus = [];
    private readonly List<DriverPackage> _packages = [];
    private readonly List<ServiceInfo> _services = [];
    private readonly List<ProcessInfo> _overlays = [];
    private readonly List<GpuEvent> _events = [];
    private readonly List<CrashArtifact> _crashes = [];
    private readonly List<KeyValuePair<string, PcieLink>> _links = [];

    private GraphicsSettings _settings = DefaultSettings();
    private bool _gpusUsable = true;
    private bool _packagesUsable = true;

    public static GraphicsSettings DefaultSettings() => new(
        RegistrySetting.Absent("TdrLevel", 3),
        RegistrySetting.Absent("TdrDelay", 2),
        RegistrySetting.Absent("TdrDdiDelay", 5),
        RegistrySetting.Absent("HwSchMode", 2),
        RegistrySetting.Absent("OverlayTestMode", 0));

    public SnapshotBuilder WithGpu(string name, GpuVendor vendor, string deviceId = @"PCI\VEN_10DE&DEV_0001")
    {
        _gpus.Add(new GpuInfo(name, vendor, deviceId, "32.0.0.1",
            DateTimeOffset.UnixEpoch, "oem1.inf", IsSigned: true, 8L * 1024 * 1024 * 1024, "OK"));
        return this;
    }

    public SnapshotBuilder WithUnusableGpus()
    {
        _gpusUsable = false;
        return this;
    }

    public SnapshotBuilder WithUnusablePackages()
    {
        _packagesUsable = false;
        return this;
    }

    public SnapshotBuilder WithPackage(
        string published, string original, string provider, string version, bool signed = true) =>
        WithPackageCore(published, original, provider, version, signed);

    private SnapshotBuilder WithPackageCore(
        string published, string original, string provider, string version, bool signed)
    {
        _packages.Add(new DriverPackage(
            published, original, provider, "Display",
            "{4d36e968-e325-11ce-bfc1-08002be10318}", version, DateTimeOffset.UnixEpoch,
            signed ? "Microsoft Windows Hardware Compatibility Publisher" : null));
        return this;
    }

    public SnapshotBuilder WithService(string name, GpuVendor vendor, string state, string startMode)
    {
        _services.Add(new ServiceInfo(name, name, state, startMode, @"C:\svc.exe", vendor));
        return this;
    }

    public SnapshotBuilder WithOverlay(string name, int id = 1)
    {
        _overlays.Add(new ProcessInfo(id, name, @"C:\app.exe", "Editeur"));
        return this;
    }

    public SnapshotBuilder WithTimeoutRecovery(int count)
    {
        for (var i = 0; i < count; i++)
        {
            _events.Add(new GpuEvent(
                DateTimeOffset.Now.AddDays(-i), 4101, "Display", "Avertissement",
                "Le pilote d'affichage a cesse de repondre et a ete recupere."));
        }

        return this;
    }

    public SnapshotBuilder WithCrashDump()
    {
        _crashes.Add(new CrashArtifact(
            CrashArtifactKind.LiveKernelReport, @"C:\dump.dmp", "dump.dmp",
            1024, DateTimeOffset.Now, "WATCHDOG"));
        return this;
    }

    public SnapshotBuilder WithPcieLink(string adapter, uint current, uint max, uint speed = 4)
    {
        _links.Add(new KeyValuePair<string, PcieLink>(adapter, new PcieLink(current, max, speed, speed)));
        return this;
    }

    public SnapshotBuilder WithUnknownPcieLink(string adapter)
    {
        _links.Add(new KeyValuePair<string, PcieLink>(adapter, PcieLink.Unknown));
        return this;
    }

    public SnapshotBuilder WithSetting(string name, object value)
    {
        _settings = name switch
        {
            "TdrLevel" => _settings with { TdrLevel = RegistrySetting.Present("TdrLevel", value, 3) },
            "TdrDelay" => _settings with { TdrDelay = RegistrySetting.Present("TdrDelay", value, 2) },
            _ => _settings,
        };

        return this;
    }

    public SystemSnapshot Build()
    {
        static Fragment<T> Ok<T>(string source, T value) =>
            Fragment<T>.Ok(source, value, TimeSpan.FromMilliseconds(1));

        static Fragment<T> Failed<T>(string source) =>
            Fragment<T>.Failed(source, "source indisponible", TimeSpan.Zero);

        return new SystemSnapshot(
            DateTimeOffset.Now, "BANC-TEST", "10.0.26100",
            _gpusUsable
                ? Ok<IReadOnlyList<GpuInfo>>("gpu", _gpus)
                : Failed<IReadOnlyList<GpuInfo>>("gpu"),
            _packagesUsable
                ? Ok<IReadOnlyList<DriverPackage>>("magasin-pilotes", _packages)
                : Failed<IReadOnlyList<DriverPackage>>("magasin-pilotes"),
            Ok<IReadOnlyList<ServiceInfo>>("services", _services),
            Ok("reglages-graphiques", _settings),
            Ok<IReadOnlyList<ProcessInfo>>("processus-overlay", _overlays),
            Ok<IReadOnlyList<GpuEvent>>("evenements-gpu", _events),
            Ok<IReadOnlyList<CrashArtifact>>("traces-plantage", _crashes),
            Ok<IReadOnlyList<KeyValuePair<string, PcieLink>>>("materiel-pcie", _links),
            Ok("alimentation", new PowerProfile("guid", "Equilibre",
                RegistrySetting.Absent("ACSettingIndex", 2),
                RegistrySetting.Absent("DCSettingIndex", 2))),
            Ok<IReadOnlyList<DisplayMode>>("ecrans", []));
    }
}
