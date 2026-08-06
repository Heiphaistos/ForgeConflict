using ForgeConflict.Core.Inventory;
using ForgeConflict.Core.Uninstall;

namespace ForgeConflict.Tests.Uninstall;

public sealed class UninstallStateTests
{
    private static UninstallState Sample() =>
        UninstallState.Start(GpuVendor.Nvidia, ["oem113.inf"], ["nvlddmkm"]);

    [Fact]
    public void Start_BeginsIdleWithNothingDone()
    {
        var state = Sample();

        Assert.Equal(UninstallPhase.Idle, state.Phase);
        Assert.Empty(state.RemovedItems);
        Assert.Empty(state.Failures);
        Assert.False(state.SafeBootExitArmed);
    }

    [Fact]
    public void RoundTrip_SurvivesSerialisation()
    {
        // L'etat traverse deux redemarrages : il doit se relire a l'identique.
        var state = Sample().With(UninstallPhase.Cleaning).Removed("paquet oem113.inf");

        var restored = UninstallState.FromJson(state.ToJson());

        Assert.NotNull(restored);
        Assert.Equal(UninstallPhase.Cleaning, restored!.Phase);
        Assert.Equal(GpuVendor.Nvidia, restored.Vendor);
        Assert.Single(restored.RemovedItems);
        Assert.Equal("oem113.inf", restored.TargetInfPackages.Single());
    }

    [Fact]
    public void FromJson_ReturnsNullOnCorruptedStateRatherThanThrowing()
    {
        // Une coupure en pleine ecriture ne doit pas empecher l'application de
        // demarrer : c'est justement le moment ou elle est indispensable.
        Assert.Null(UninstallState.FromJson("{ tronque"));
    }

    [Fact]
    public void IsInProgress_IsTrueOnlyBetweenStartAndReport()
    {
        Assert.False(Sample().IsInProgress);
        Assert.True(Sample().With(UninstallPhase.SafeModeBooting).IsInProgress);
        Assert.True(Sample().With(UninstallPhase.Cleaning).IsInProgress);
        Assert.False(Sample().With(UninstallPhase.Report).IsInProgress);
    }

    [Fact]
    public void Failed_AccumulatesWithoutLosingEarlierEntries()
    {
        var state = Sample().Failed("premier").Failed("second").Removed("un succes");

        Assert.Equal(2, state.Failures.Count);
        Assert.Single(state.RemovedItems);
    }
}

public sealed class DriverUninstallerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "fc-uninstall", Guid.NewGuid().ToString("N"));

    private string StatePath => Path.Combine(_dir, "uninstall.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private DriverUninstaller Uninstaller() =>
        new(StatePath, new BootConfiguration(new TestLog()), new TestLog());

    [Fact]
    public void Load_ReturnsNullWhenNoOperationWasEverStarted() =>
        Assert.Null(Uninstaller().Load());

    [Fact]
    public void SaveThenLoad_SurvivesAsIfTheMachineHadRebooted()
    {
        var uninstaller = Uninstaller();
        var state = UninstallState.Start(GpuVendor.Amd, ["oem5.inf"], ["amdkmdag"])
            .With(UninstallPhase.SafeModeBooting);

        uninstaller.Save(state);

        // Un lecteur neuf : c'est exactement ce que fait l'application au demarrage.
        var reloaded = Uninstaller().Load();

        Assert.NotNull(reloaded);
        Assert.Equal(UninstallPhase.SafeModeBooting, reloaded!.Phase);
        Assert.Equal(GpuVendor.Amd, reloaded.Vendor);
    }

    [Fact]
    public void Clear_RemovesTheStateSoNothingResumes()
    {
        var uninstaller = Uninstaller();
        uninstaller.Save(UninstallState.Start(GpuVendor.Nvidia, [], []));

        uninstaller.Clear();

        Assert.Null(Uninstaller().Load());
    }

    [Fact]
    public void ResidualFolders_AreVendorSpecificAndNeverEmptyForKnownVendors()
    {
        Assert.NotEmpty(DriverUninstaller.ResidualFolders(GpuVendor.Nvidia));
        Assert.NotEmpty(DriverUninstaller.ResidualFolders(GpuVendor.Amd));
        Assert.Empty(DriverUninstaller.ResidualFolders(GpuVendor.Unknown));
    }

    [Fact]
    public void ResidualFolders_NeverIncludeWindowsOrProgramFilesRoot()
    {
        // Un chemin trop large effacerait bien plus que le pilote. La liste doit
        // rester chirurgicale.
        foreach (var vendor in new[] { GpuVendor.Nvidia, GpuVendor.Amd, GpuVendor.Intel })
        {
            foreach (var folder in DriverUninstaller.ResidualFolders(vendor))
            {
                foreach (var forbidden in new[]
                         {
                             @"C:\", @"C:\Windows", @"C:\Program Files",
                             @"C:\Program Files (x86)", @"C:\ProgramData",
                         })
                {
                    Assert.False(string.Equals(folder, forbidden, StringComparison.OrdinalIgnoreCase),
                        $"chemin trop large : {folder}");
                }

                // Tout dossier a supprimer doit nommer son fournisseur. Un chemin qui
                // ne le fait pas designe forcement autre chose que le pilote.
                Assert.True(
                    folder.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
                    folder.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
                    folder.Contains("Intel", StringComparison.OrdinalIgnoreCase),
                    $"chemin sans nom de fournisseur, donc trop general : {folder}");
            }
        }
    }

    [Fact]
    public void ServicesFor_ListsTheVendorKernelDriver()
    {
        Assert.Contains("nvlddmkm", DriverUninstaller.ServicesFor(GpuVendor.Nvidia));
        Assert.Contains("amdkmdag", DriverUninstaller.ServicesFor(GpuVendor.Amd));
    }
}

public sealed class BootConfigurationTests
{
    [Fact]
    public void EnterSafeMode_RefusesWhenTheExitWasNotArmed()
    {
        // Le garde-fou central du desinstalleur : entrer en mode sans echec sans
        // garantie de sortie laisserait la machine bloquee en boucle. Ce test ne
        // touche a rien : il verifie que l'appel est REFUSE.
        var boot = new BootConfiguration(new TestLog());

        Assert.False(boot.EnterSafeMode(exitArmed: false));
    }

    [Fact]
    public void IsInSafeMode_IsFalseOnANormallyBootedMachine()
    {
        // Les tests ne tournent jamais en mode sans echec : si ce test echoue,
        // c'est que la detection est cassee.
        Assert.False(BootConfiguration.IsInSafeMode());
    }
}
