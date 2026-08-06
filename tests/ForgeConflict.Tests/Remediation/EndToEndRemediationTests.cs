using ForgeConflict.Core.Inventory;
using ForgeConflict.Core.Remediation;
using ForgeConflict.Core.Rules;
using ForgeConflict.Core.Safety;
using ForgeConflict.Tests.Rules;
using Microsoft.Win32;

namespace ForgeConflict.Tests.Remediation;

/// <summary>
/// Parcourt la chaine complete avec le VRAI applicateur registre et le VRAI journal
/// sur disque : apercu, accord, ecriture, watchdog, retour arriere.
///
/// La cible est une cle jetable de HKEY_CURRENT_USER. Aucune cle de pilote n'est
/// touchee : la regle « tout test d'ecriture pilote se fait en VM » reste entiere,
/// tout en prouvant que le mecanisme fonctionne reellement.
/// </summary>
public sealed class EndToEndRemediationTests : IDisposable
{
    private readonly string _subPath = $@"Software\ForgeConflict\Tests\{Guid.NewGuid():N}";
    private readonly string _journal = Path.Combine(Path.GetTempPath(), "fc-e2e", Guid.NewGuid().ToString("N"));

    private string KeyPath => $@"HKCU\{_subPath}";

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(_subPath, throwOnMissingSubKey: false);
        if (Directory.Exists(_journal)) Directory.Delete(_journal, recursive: true);
    }

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

    /// <summary>Remediation de test visant la cle jetable plutot que le registre pilote.</summary>
    private sealed class SandboxRemediation : IRemediation
    {
        private readonly string _keyPath;
        private readonly object? _previous;

        public SandboxRemediation(string keyPath, object? previous)
        {
            _keyPath = keyPath;
            _previous = previous;
        }

        public string RuleId => "FG-TEST-001";
        public string Title => "Correction de banc d'essai";
        public RiskLevel Risk => RiskLevel.Eleve;
        public bool AffectsDisplay => true;
        public bool RequiresReboot => false;
        public string Explanation => "Retire une valeur de test.";

        public bool Handles(Finding finding) => finding.RuleId == RuleId;

        public IReadOnlyList<ChangeRecord> Preview(SystemSnapshot snapshot) =>
        [
            new ChangeRecord(ChangeKind.RegistryValue, _keyPath, "TdrLevel",
                _previous, null, PreviousValueExisted: _previous is not null, AffectsDisplay: true),
        ];
    }

    private RemediationService Service() => new(
        new FileTransactionStore(_journal),
        new RegistryChangeApplier(new TestLog()),
        new TestLog());

    [Fact]
    public async Task ConfirmedRemediation_ActuallyChangesTheRegistry()
    {
        Seed("TdrLevel", 0);

        var result = await Service().ApplyAsync(
            new SandboxRemediation(KeyPath, 0), new SnapshotBuilder().Build(),
            confirm: _ => Task.FromResult(true),
            confirmDisplay: () => Task.FromResult(true));

        Assert.Equal(RemediationOutcome.Appliquee, result.Outcome);
        Assert.Null(Read("TdrLevel"));
    }

    [Fact]
    public async Task DeclinedRemediation_LeavesTheRegistryUntouched()
    {
        Seed("TdrLevel", 0);

        var result = await Service().ApplyAsync(
            new SandboxRemediation(KeyPath, 0), new SnapshotBuilder().Build(),
            confirm: _ => Task.FromResult(false));

        Assert.Equal(RemediationOutcome.Refusee, result.Outcome);
        Assert.Equal(0, Read("TdrLevel"));
    }

    [Fact]
    public async Task WatchdogTimeout_PutsTheRegistryBackAsItWas()
    {
        // Le scenario de l'ecran noir, joue de bout en bout : la valeur est ecrite,
        // personne ne confirme, et la machine retrouve son etat initial.
        Seed("TdrLevel", 0);
        var never = new TaskCompletionSource<bool>();

        var result = await Service().ApplyAsync(
            new SandboxRemediation(KeyPath, 0), new SnapshotBuilder().Build(),
            confirm: _ => Task.FromResult(true),
            confirmDisplay: () => never.Task,
            watchdogDelay: TimeSpan.FromMilliseconds(300));

        Assert.Equal(RemediationOutcome.AnnuleeParWatchdog, result.Outcome);
        Assert.Equal(0, Read("TdrLevel"));
    }

    [Fact]
    public async Task TheUndoPlanIsOnDiskBeforeTheRegistryIsTouched()
    {
        // L'ordre est verifie en observant le journal AU MOMENT ou l'accord est
        // donne : la transaction doit deja y figurer, ouverte.
        Seed("TdrLevel", 0);
        var store = new FileTransactionStore(_journal);
        var service = new RemediationService(store, new RegistryChangeApplier(new TestLog()), new TestLog());

        IReadOnlyList<Transaction> duringConfirm = [];

        await service.ApplyAsync(
            new SandboxRemediation(KeyPath, 0), new SnapshotBuilder().Build(),
            confirm: _ => Task.FromResult(true),
            confirmDisplay: () =>
            {
                // A cet instant l'ecriture a eu lieu : le plan doit exister sur disque.
                duringConfirm = store.ReadAll();
                return Task.FromResult(true);
            });

        var transaction = Assert.Single(duringConfirm);
        Assert.Equal("FG-TEST-001", transaction.RuleId);
        Assert.Single(transaction.Changes);
        Assert.True(transaction.Changes[0].PreviousValueExisted);
        Assert.Equal(0, transaction.Changes[0].PreviousValue);
    }

    [Fact]
    public async Task AnInterruptedRemediationIsUndoneOnTheNextStart()
    {
        // Simule une coupure : la transaction est ecrite et appliquee, mais rien ne
        // la valide. Au demarrage suivant, la reprise doit rendre la valeur.
        Seed("TdrLevel", 0);

        var store = new FileTransactionStore(_journal);
        var applier = new RegistryChangeApplier(new TestLog());
        var remediation = new SandboxRemediation(KeyPath, 0);
        var transaction = remediation.BuildTransaction(new SnapshotBuilder().Build());

        store.Write(transaction);
        foreach (var change in transaction.Changes) applier.Apply(change);

        Assert.Null(Read("TdrLevel"));

        var recovered = new RecoveryService(
            new FileTransactionStore(_journal), applier, new TestLog()).RecoverAutomatically();

        Assert.Equal(1, recovered);
        Assert.Equal(0, Read("TdrLevel"));

        await Task.CompletedTask;
    }

    [Fact]
    public async Task RecoveryRestoresAbsenceRatherThanWritingZero()
    {
        // La valeur n'existait pas : apres correction puis restauration, elle doit
        // avoir disparu, pas valoir zero.
        var store = new FileTransactionStore(_journal);
        var applier = new RegistryChangeApplier(new TestLog());

        var change = ChangeRecord.RegistryValue(KeyPath, "TdrDelay", previousValue: null, newValue: 60);
        var transaction = Transaction.Open("FG-TEST-002", "Ecrire puis defaire", RiskLevel.Eleve, [change]);

        store.Write(transaction);
        applier.Apply(change);
        Assert.Equal(60, Read("TdrDelay"));

        new RecoveryService(new FileTransactionStore(_journal), applier, new TestLog())
            .RecoverAutomatically();

        Assert.Null(Read("TdrDelay"));

        await Task.CompletedTask;
    }
}
