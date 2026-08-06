using ForgeConflict.Core.Inventory;
using ForgeConflict.Core.Rules;
using ForgeConflict.Core.Rules.Catalog;

namespace ForgeConflict.Tests.Rules;

public sealed class DuplicateDriverPackageRuleTests
{
    [Fact]
    public void Evaluate_FindsNothingWhenEachInfAppearsOnce()
    {
        var snapshot = new SnapshotBuilder()
            .WithPackage("oem17.inf", "iigd_dch.inf", "Intel Corporation", "32.0.101.7088")
            .WithPackage("oem113.inf", "nvaci.inf", "NVIDIA", "32.0.15.9174")
            .Build();

        Assert.Empty(new DuplicateDriverPackageRule().Evaluate(snapshot));
    }

    [Fact]
    public void Evaluate_FlagsTwoPackagesFromTheSameInf()
    {
        // Cas reel constate sur la machine de reference le 06/08/2026.
        var snapshot = new SnapshotBuilder()
            .WithPackage("oem17.inf", "iigd_dch.inf", "Intel Corporation", "32.0.101.7088")
            .WithPackage("oem154.inf", "iigd_dch.inf", "Intel Corporation", "30.0.100.9864")
            .Build();

        var finding = Assert.Single(new DuplicateDriverPackageRule().Evaluate(snapshot));

        Assert.Equal(Severity.Moyen, finding.Severity);
        Assert.Contains("oem154.inf", finding.SuggestedFix!, StringComparison.Ordinal);
        Assert.DoesNotContain("oem17.inf", finding.SuggestedFix!, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_ComparesVersionsNumericallyNotAsText()
    {
        // « 9.x » est plus recent que « 30.x » en comparaison textuelle : la regle
        // designerait alors le mauvais paquet comme actif et proposerait de
        // supprimer le bon.
        var snapshot = new SnapshotBuilder()
            .WithPackage("oemA.inf", "meme.inf", "Vendeur", "9.0.0.1")
            .WithPackage("oemB.inf", "meme.inf", "Vendeur", "30.0.0.1")
            .Build();

        var finding = Assert.Single(new DuplicateDriverPackageRule().Evaluate(snapshot));

        Assert.Contains("oemA.inf", finding.SuggestedFix!, StringComparison.Ordinal);
        Assert.DoesNotContain("oemB.inf", finding.SuggestedFix!, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_ReturnsNothingWhenTheSourceIsUnusable()
    {
        var snapshot = new SnapshotBuilder().WithUnusablePackages().Build();

        Assert.Empty(new DuplicateDriverPackageRule().Evaluate(snapshot));
    }
}

public sealed class PcieLinkRuleTests
{
    [Fact]
    public void Evaluate_IgnoresAFullWidthLink()
    {
        var snapshot = new SnapshotBuilder()
            .WithGpu("Carte", GpuVendor.Nvidia)
            .WithPcieLink("Carte", current: 16, max: 16)
            .Build();

        Assert.Empty(new PcieLinkRule().Evaluate(snapshot));
    }

    [Fact]
    public void Evaluate_IgnoresAnUnknownLinkRatherThanCallingItDegraded()
    {
        // Un GPU integre n'expose aucune propriete PCIe : l'absence d'information
        // ne doit jamais devenir une alerte.
        var snapshot = new SnapshotBuilder()
            .WithGpu("Intel UHD", GpuVendor.Intel)
            .WithUnknownPcieLink("Intel UHD")
            .Build();

        Assert.Empty(new PcieLinkRule().Evaluate(snapshot));
    }

    [Fact]
    public void Evaluate_TreatsADegradedLinkOnALaptopAsInformationOnly()
    {
        // Configuration de la machine de reference : x8 pour un maximum x16, ce qui
        // est le cablage voulu par le constructeur et non un defaut.
        var snapshot = new SnapshotBuilder()
            .WithGpu("Intel UHD Graphics", GpuVendor.Intel)
            .WithGpu("NVIDIA GeForce RTX 3070 Laptop GPU", GpuVendor.Nvidia)
            .WithPcieLink("NVIDIA GeForce RTX 3070 Laptop GPU", current: 8, max: 16)
            .Build();

        var finding = Assert.Single(new PcieLinkRule().Evaluate(snapshot));

        Assert.Equal(Severity.Information, finding.Severity);
        Assert.Null(finding.SuggestedFix);
    }

    [Fact]
    public void Evaluate_TreatsADegradedLinkOnADesktopAsAProblem()
    {
        var snapshot = new SnapshotBuilder()
            .WithGpu("NVIDIA GeForce RTX 4080", GpuVendor.Nvidia)
            .WithPcieLink("NVIDIA GeForce RTX 4080", current: 4, max: 16)
            .Build();

        var finding = Assert.Single(new PcieLinkRule().Evaluate(snapshot));

        Assert.Equal(Severity.Moyen, finding.Severity);
        Assert.True(finding.HasFix);
    }
}

public sealed class TdrSettingsRuleTests
{
    [Fact]
    public void Evaluate_FindsNothingWhenSettingsAreAtTheirDefaults()
    {
        // Cas de la machine saine : les cles n'existent pas.
        Assert.Empty(new TdrSettingsRule().Evaluate(new SnapshotBuilder().Build()));
    }

    [Fact]
    public void Evaluate_FlagsDisabledRecoveryAsCritical()
    {
        var snapshot = new SnapshotBuilder().WithSetting("TdrLevel", 0).Build();

        var finding = Assert.Single(new TdrSettingsRule().Evaluate(snapshot));

        Assert.Equal(Severity.Critique, finding.Severity);
        Assert.Contains("fige", finding.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_FlagsAnExcessiveDelay()
    {
        var snapshot = new SnapshotBuilder().WithSetting("TdrDelay", 60).Build();

        var finding = Assert.Single(new TdrSettingsRule().Evaluate(snapshot));

        Assert.Equal(Severity.Moyen, finding.Severity);
        Assert.Contains("60", finding.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_AcceptsAModestDelayWithoutComplaining()
    {
        var snapshot = new SnapshotBuilder().WithSetting("TdrDelay", 8).Build();

        Assert.Empty(new TdrSettingsRule().Evaluate(snapshot));
    }

    [Fact]
    public void Evaluate_NeverReadsAnAbsentLevelAsDisabled()
    {
        // Le piege central du projet : TdrLevel absent signifie « defaut 3 », donc
        // recuperation active. Le lire comme zero declencherait une alerte critique
        // parfaitement fausse sur toute machine saine.
        Assert.Empty(new TdrSettingsRule().Evaluate(new SnapshotBuilder().Build()));
    }
}

public sealed class OverlayConflictRuleTests
{
    [Fact]
    public void Evaluate_StaysSilentBelowTheNoticeableCount()
    {
        var snapshot = new SnapshotBuilder().WithOverlay("Discord").Build();

        Assert.Empty(new OverlayConflictRule().Evaluate(snapshot));
    }

    [Fact]
    public void Evaluate_GroupsProcessesByApplication()
    {
        // Une application peut compter une dizaine de processus : les compter
        // individuellement gonflerait artificiellement le nombre d'overlays.
        var snapshot = new SnapshotBuilder()
            .WithOverlay("Discord", 1).WithOverlay("Discord", 2).WithOverlay("Discord", 3)
            .Build();

        Assert.Empty(new OverlayConflictRule().Evaluate(snapshot));
    }

    [Fact]
    public void Evaluate_ReportsInformationWhenSeveralOverlaysCoexistWithoutIncident()
    {
        var snapshot = new SnapshotBuilder()
            .WithOverlay("Discord", 1).WithOverlay("obs64", 2).WithOverlay("rtss", 3)
            .Build();

        var finding = Assert.Single(new OverlayConflictRule().Evaluate(snapshot));

        Assert.Equal(Severity.Information, finding.Severity);
        Assert.Null(finding.SuggestedFix);
    }

    [Fact]
    public void Evaluate_RaisesSeverityWhenCrashesWereRecorded()
    {
        var snapshot = new SnapshotBuilder()
            .WithOverlay("Discord", 1).WithOverlay("obs64", 2)
            .WithTimeoutRecovery(2)
            .Build();

        var finding = Assert.Single(new OverlayConflictRule().Evaluate(snapshot));

        Assert.Equal(Severity.Eleve, finding.Severity);
        Assert.True(finding.HasFix);
    }
}

public sealed class GenericDriverRuleTests
{
    [Fact]
    public void Evaluate_FlagsAnAdapterLeftOnTheBasicMicrosoftDriver()
    {
        var snapshot = new SnapshotBuilder()
            .WithGpu("Microsoft Basic Display Adapter", GpuVendor.Microsoft, @"ROOT\BASICDISPLAY")
            .Build();

        var finding = Assert.Single(new GenericDriverRule().Evaluate(snapshot));

        Assert.Equal(Severity.Eleve, finding.Severity);
    }

    [Fact]
    public void Evaluate_IgnoresAVirtualisedAdapter()
    {
        // Dans une machine virtuelle, l'adaptateur generique est normal : le
        // signaler serait une fausse alerte systematique.
        var snapshot = new SnapshotBuilder()
            .WithGpu("Microsoft Hyper-V Video", GpuVendor.Microsoft, @"ROOT\VMBUS")
            .Build();

        Assert.Empty(new GenericDriverRule().Evaluate(snapshot));
    }

    [Fact]
    public void Evaluate_IgnoresAVendorDriver()
    {
        var snapshot = new SnapshotBuilder()
            .WithGpu("NVIDIA GeForce RTX 3070", GpuVendor.Nvidia)
            .Build();

        Assert.Empty(new GenericDriverRule().Evaluate(snapshot));
    }
}

public sealed class CrashHistoryRuleTests
{
    [Fact]
    public void Evaluate_FindsNothingOnAHealthyMachine() =>
        Assert.Empty(new CrashHistoryRule().Evaluate(new SnapshotBuilder().Build()));

    [Fact]
    public void Evaluate_TreatsAnIsolatedRecoveryAsModerate()
    {
        var snapshot = new SnapshotBuilder().WithTimeoutRecovery(1).Build();

        var finding = Assert.Single(new CrashHistoryRule().Evaluate(snapshot));

        Assert.Equal(Severity.Moyen, finding.Severity);
        Assert.Null(finding.SuggestedFix);
    }

    [Fact]
    public void Evaluate_TreatsRepeatedRecoveriesAsSerious()
    {
        var snapshot = new SnapshotBuilder().WithTimeoutRecovery(5).Build();

        var finding = Assert.Single(new CrashHistoryRule().Evaluate(snapshot));

        Assert.Equal(Severity.Eleve, finding.Severity);
        Assert.True(finding.HasFix);
    }

    [Fact]
    public void Evaluate_ReportsKernelDumpsSeparatelyFromEvents()
    {
        var snapshot = new SnapshotBuilder().WithTimeoutRecovery(1).WithCrashDump().Build();

        Assert.Equal(2, new CrashHistoryRule().Evaluate(snapshot).Count);
    }
}

public sealed class UnsignedDriverRuleTests
{
    [Fact]
    public void Evaluate_IgnoresSignedPackages()
    {
        var snapshot = new SnapshotBuilder()
            .WithPackage("oem1.inf", "a.inf", "NVIDIA", "1.0.0.0")
            .Build();

        Assert.Empty(new UnsignedDriverRule().Evaluate(snapshot));
    }

    [Fact]
    public void Evaluate_FlagsAnUnsignedPackage()
    {
        var snapshot = new SnapshotBuilder()
            .WithPackage("oem9.inf", "b.inf", "Inconnu", "1.0.0.0", signed: false)
            .Build();

        var finding = Assert.Single(new UnsignedDriverRule().Evaluate(snapshot));

        Assert.Equal(Severity.Eleve, finding.Severity);
        Assert.Contains("oem9.inf", finding.Evidence, StringComparison.Ordinal);
    }
}

public sealed class StoppedVendorServiceRuleTests
{
    [Fact]
    public void Evaluate_IgnoresAManualServiceThatIsStopped()
    {
        // Un service en demarrage manuel et arrete est parfaitement normal :
        // le signaler produirait du bruit sur toute machine.
        var snapshot = new SnapshotBuilder()
            .WithService("nvagent", GpuVendor.Nvidia, "Stopped", "Manual")
            .Build();

        Assert.Empty(new StoppedVendorServiceRule().Evaluate(snapshot));
    }

    [Fact]
    public void Evaluate_IgnoresAnAutomaticServiceThatIsRunning()
    {
        var snapshot = new SnapshotBuilder()
            .WithService("NVDisplay.ContainerLocalSystem", GpuVendor.Nvidia, "Running", "Auto")
            .Build();

        Assert.Empty(new StoppedVendorServiceRule().Evaluate(snapshot));
    }

    [Fact]
    public void Evaluate_FlagsAnAutomaticServiceThatFailedToStart()
    {
        var snapshot = new SnapshotBuilder()
            .WithService("NVDisplay.ContainerLocalSystem", GpuVendor.Nvidia, "Stopped", "Auto")
            .Build();

        var finding = Assert.Single(new StoppedVendorServiceRule().Evaluate(snapshot));

        Assert.Equal(Severity.Moyen, finding.Severity);
    }
}

public sealed class HybridGpuRuleTests
{
    [Fact]
    public void Evaluate_RecognisesAHybridConfiguration()
    {
        var snapshot = new SnapshotBuilder()
            .WithGpu("Intel UHD Graphics", GpuVendor.Intel)
            .WithGpu("NVIDIA GeForce RTX 3070 Laptop GPU", GpuVendor.Nvidia)
            .Build();

        var finding = Assert.Single(new HybridGpuRule().Evaluate(snapshot));

        Assert.Equal(Severity.Information, finding.Severity);
        Assert.False(finding.HasFix);
    }

    [Fact]
    public void Evaluate_SaysNothingOnASingleGpuMachine()
    {
        var snapshot = new SnapshotBuilder()
            .WithGpu("NVIDIA GeForce RTX 4080", GpuVendor.Nvidia)
            .Build();

        Assert.Empty(new HybridGpuRule().Evaluate(snapshot));
    }
}
