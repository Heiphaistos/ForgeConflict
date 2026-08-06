using ForgeConflict.Core.Safety;
using Microsoft.Win32;

namespace ForgeConflict.Tests.Remediation;

/// <summary>
/// Ces tests ecrivent dans le registre, mais uniquement sous une cle jetable de
/// HKEY_CURRENT_USER. Aucune cle de pilote, aucun HKLM : la regle « tout test
/// d'ecriture pilote se fait en VM » reste entiere.
/// </summary>
public sealed class RegistryChangeApplierTests : IDisposable
{
    private readonly string _subPath = $@"Software\ForgeConflict\Tests\{Guid.NewGuid():N}";

    private string KeyPath => $@"HKCU\{_subPath}";

    public void Dispose() => Registry.CurrentUser.DeleteSubKeyTree(_subPath, throwOnMissingSubKey: false);

    private object? Read(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(_subPath);
        return key?.GetValue(name);
    }

    private void Seed(string name, int value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(_subPath, writable: true);
        key!.SetValue(name, value, RegistryValueKind.DWord);
    }

    [Fact]
    public void Apply_WritesTheNewValue()
    {
        var applier = new RegistryChangeApplier(new TestLog());

        applier.Apply(ChangeRecord.RegistryValue(KeyPath, "TdrDelay", null, 8));

        Assert.Equal(8, Read("TdrDelay"));
    }

    [Fact]
    public void Apply_WithANullValueRemovesTheEntry()
    {
        Seed("TdrDelay", 8);
        var applier = new RegistryChangeApplier(new TestLog());

        applier.Apply(new ChangeRecord(
            ChangeKind.RegistryValue, KeyPath, "TdrDelay", 8, null, true, true));

        Assert.Null(Read("TdrDelay"));
    }

    [Fact]
    public void Revert_RestoresThePreviousValue()
    {
        Seed("TdrDelay", 2);
        var applier = new RegistryChangeApplier(new TestLog());
        var change = ChangeRecord.RegistryValue(KeyPath, "TdrDelay", 2, 8);

        applier.Apply(change);
        Assert.Equal(8, Read("TdrDelay"));

        applier.Revert(change);
        Assert.Equal(2, Read("TdrDelay"));
    }

    [Fact]
    public void Revert_DeletesAValueThatNeverExistedInsteadOfWritingZero()
    {
        // Le piege central du lot : ecrire zero imposerait une valeur que la machine
        // n'a jamais eue. Seule la suppression rend a Windows son propre defaut.
        var applier = new RegistryChangeApplier(new TestLog());
        var change = ChangeRecord.RegistryValue(KeyPath, "TdrLevel", previousValue: null, newValue: 0);

        applier.Apply(change);
        Assert.Equal(0, Read("TdrLevel"));

        applier.Revert(change);
        Assert.Null(Read("TdrLevel"));
    }

    [Fact]
    public void Revert_IsSafeToRunTwice()
    {
        // La reprise au demarrage peut rejouer une restauration deja effectuee :
        // elle ne doit pas echouer pour autant.
        var applier = new RegistryChangeApplier(new TestLog());
        var change = ChangeRecord.RegistryValue(KeyPath, "TdrLevel", null, 0);

        applier.Apply(change);
        applier.Revert(change);

        Assert.Null(Record.Exception(() => applier.Revert(change)));
    }

    [Fact]
    public void Apply_RejectsAnUnknownHive()
    {
        var applier = new RegistryChangeApplier(new TestLog());

        Assert.Throws<ArgumentException>(() =>
            applier.Apply(ChangeRecord.RegistryValue(@"HKXX\Rien", "A", null, 1)));
    }

    [Fact]
    public void Apply_RejectsAChangeThatIsNotARegistryValue()
    {
        var applier = new RegistryChangeApplier(new TestLog());
        var change = new ChangeRecord(
            ChangeKind.ServiceStartMode, "nvagent", null, "Auto", "Manual", true, false);

        Assert.Throws<NotSupportedException>(() => applier.Apply(change));
    }
}
