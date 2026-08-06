using ForgeConflict.Core.Inventory.Collectors;
using ForgeConflict.Core.Inventory.Model;
using ForgeConflict.Native;

namespace ForgeConflict.Tests.Inventory;

public sealed class GpuEventCollectorTests
{
    [Fact]
    public void BuildQuery_FiltersOnProviderAndIdTogether()
    {
        var query = GpuEventCollector.BuildQuery();

        // Un identifiant d'evenement n'est unique QUE par fournisseur : filtrer sur
        // l'identifiant seul ramene WindowsUpdateClient id 19 et BTHUSB id 18, sans
        // aucun rapport avec le GPU.
        Assert.Contains("Provider[", query, StringComparison.Ordinal);
        Assert.Contains("EventID=", query, StringComparison.Ordinal);
        Assert.Contains("nvlddmkm", query, StringComparison.Ordinal);
        Assert.Contains("amdkmdag", query, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildQuery_NeverSelectsAnEventIdOutsideAProviderClause()
    {
        var query = GpuEventCollector.BuildQuery();

        // Chaque terme EventID doit etre precede d'un selecteur Provider dans la
        // meme clause : sinon le filtre ramenerait n'importe quel composant Windows.
        foreach (var clause in query.Split(") or (", StringSplitOptions.RemoveEmptyEntries))
        {
            if (clause.Contains("EventID=", StringComparison.Ordinal))
            {
                Assert.Contains("Provider[@Name=", clause, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void Collect_ReturnsOnlyGraphicsProvidersNeverUnrelatedNoise()
    {
        // Une machine saine n'a aucun evenement : l'absence est un resultat valide.
        // Mais si des evenements remontent, aucun ne doit venir d'un composant
        // etranger au GPU.
        var events = new GpuEventCollector().Collect(CancellationToken.None);

        Assert.NotNull(events);
        Assert.All(events, e => Assert.False(string.IsNullOrWhiteSpace(e.ProviderName)));
        Assert.DoesNotContain(events, e =>
            e.ProviderName.Contains("WindowsUpdate", StringComparison.OrdinalIgnoreCase) ||
            e.ProviderName.Contains("BTHUSB", StringComparison.OrdinalIgnoreCase) ||
            e.ProviderName.Contains("TPM", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Metadata_IsStableAndUsable()
    {
        var collector = new GpuEventCollector();

        Assert.Equal("evenements-gpu", collector.Source);
        Assert.True(collector.Timeout > TimeSpan.Zero);
    }
}

public sealed class CrashArtifactCollectorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fc-crash", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Scan_ReadsTheCategoryFromTheContainingFolder()
    {
        var watchdog = Path.Combine(_root, "WATCHDOG");
        Directory.CreateDirectory(watchdog);
        File.WriteAllText(Path.Combine(watchdog, "WATCHDOG-20260806-0001.dmp"), "contenu");

        var artifacts = CrashArtifactCollector.Scan(_root, CrashArtifactKind.LiveKernelReport);

        var artifact = Assert.Single(artifacts);
        Assert.Equal("WATCHDOG", artifact.Category);
        Assert.Equal(CrashArtifactKind.LiveKernelReport, artifact.Kind);
        Assert.True(artifact.SizeBytes > 0);
    }

    [Fact]
    public void Scan_ReturnsEmptyWhenTheFolderDoesNotExist()
    {
        // Cas normal sur une machine saine : ni LiveKernelReports ni Minidump.
        var artifacts = CrashArtifactCollector.Scan(
            Path.Combine(_root, "absent"), CrashArtifactKind.Minidump);

        Assert.Empty(artifacts);
    }

    [Fact]
    public void Scan_IgnoresFilesThatAreNotDumps()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "notes.txt"), "pas un dump");
        File.WriteAllText(Path.Combine(_root, "crash.dmp"), "dump");

        var artifact = Assert.Single(CrashArtifactCollector.Scan(_root, CrashArtifactKind.Minidump));
        Assert.Equal("crash.dmp", artifact.FileName);
    }

    [Fact]
    public void Collect_NeverThrowsOnTheRealMachine()
    {
        var artifacts = new CrashArtifactCollector().Collect(CancellationToken.None);

        Assert.NotNull(artifacts);
        Assert.All(artifacts, a => Assert.False(string.IsNullOrWhiteSpace(a.FileName)));
    }

    [Fact]
    public void Metadata_IsStableAndUsable()
    {
        var collector = new CrashArtifactCollector();

        Assert.Equal("traces-plantage", collector.Source);
        Assert.True(collector.Timeout > TimeSpan.Zero);
    }
}

public sealed class PcieCollectorTests
{
    private sealed class FakeReader : IDevicePropertyReader
    {
        private readonly Dictionary<uint, uint> _values;

        public FakeReader(Dictionary<uint, uint> values) => _values = values;

        public bool TryReadUInt32(string? instanceId, DeviceProperty property, out uint value) =>
            _values.TryGetValue(property.PropertyId, out value);

        public bool TryReadString(string? instanceId, DeviceProperty property, out string? value)
        {
            value = null;
            return false;
        }
    }

    [Fact]
    public void ReadLink_BuildsADegradedLinkFromDeviceProperties()
    {
        var reader = new FakeReader(new Dictionary<uint, uint>
        {
            [10] = 8,   // CurrentLinkWidth
            [12] = 16,  // MaxLinkWidth
            [9] = 4,    // CurrentLinkSpeed
            [11] = 4,   // MaxLinkSpeed
        });

        var link = PcieCollector.ReadLink(reader, @"PCI\VEN_10DE&DEV_249D");

        Assert.True(link.IsKnown);
        Assert.True(link.IsWidthDegraded);
        Assert.Equal(8u, link.CurrentWidth);
        Assert.Equal(16u, link.MaxWidth);
    }

    [Fact]
    public void ReadLink_ReturnsUnknownWhenNoPropertyCanBeRead()
    {
        // Cas du GPU integre et de la machine virtuelle : rien n'est expose.
        var link = PcieCollector.ReadLink(new FakeReader([]), @"ROOT\BASICDISPLAY");

        Assert.False(link.IsKnown);
        Assert.False(link.IsDegraded);
    }

    [Fact]
    public void Collect_ReportsOneEntryPerAdapterOnTheRealMachine()
    {
        var links = new PcieCollector().Collect(CancellationToken.None);

        Assert.NotEmpty(links);
        Assert.All(links, l => Assert.False(string.IsNullOrWhiteSpace(l.Key)));
    }

    [Fact]
    public void Metadata_IsStableAndUsable()
    {
        var collector = new PcieCollector();

        Assert.Equal("materiel-pcie", collector.Source);
        Assert.True(collector.Timeout > TimeSpan.Zero);
    }
}

public sealed class PowerAndDisplayCollectorTests
{
    [Fact]
    public void PowerCollector_ReadsTheActiveSchemeOnTheRealMachine()
    {
        var profile = new PowerCollector().Collect(CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(profile.ActiveSchemeGuid));
        Assert.False(string.IsNullOrWhiteSpace(profile.ActiveSchemeName));
    }

    [Fact]
    public void PowerCollector_NeverReportsAbsentLinkStateAsZero()
    {
        var profile = new PowerCollector().Collect(CancellationToken.None);

        // La valeur par defaut est l'economie maximale (2) : la lire comme zero
        // ferait croire que l'economie d'energie PCIe est desactivee.
        if (!profile.PciExpressLinkStateOnAc.IsPresent)
        {
            Assert.NotNull(profile.PciExpressLinkStateOnAc.EffectiveValue);
            Assert.True(profile.PciExpressLinkStateOnAc.IsDefault);
        }
    }

    [Fact]
    public void PowerCollector_MetadataIsStable()
    {
        var collector = new PowerCollector();

        Assert.Equal("alimentation", collector.Source);
        Assert.True(collector.Timeout > TimeSpan.Zero);
    }

    [Fact]
    public void DisplayCollector_ReportsActiveModesOnTheRealMachine()
    {
        var modes = new DisplayCollector().Collect(CancellationToken.None);

        Assert.NotEmpty(modes);
        Assert.All(modes, m => Assert.False(string.IsNullOrWhiteSpace(m.AdapterName)));

        // Au moins un adaptateur pilote un affichage : sinon rien ne serait visible.
        Assert.Contains(modes, m => m.IsActive);
    }

    [Fact]
    public void DisplayCollector_MetadataIsStable()
    {
        var collector = new DisplayCollector();

        Assert.Equal("ecrans", collector.Source);
        Assert.True(collector.Timeout > TimeSpan.Zero);
    }
}
