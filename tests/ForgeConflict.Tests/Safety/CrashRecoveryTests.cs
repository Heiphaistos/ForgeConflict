using ForgeConflict.Core.Safety;

namespace ForgeConflict.Tests.Safety;

public sealed class RecoveryServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "fc-recovery", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private sealed class CountingApplier : IChangeApplier
    {
        public int Reverted { get; private set; }

        public void Apply(ChangeRecord change) { }

        public void Revert(ChangeRecord change) => Reverted++;
    }

    private static Transaction Display(bool affectsDisplay) => Transaction.Open(
        "FG-TDR-001", "Allonger le delai TDR", RiskLevel.Eleve,
        [ChangeRecord.RegistryValue(@"HKLM\Test", "TdrDelay", null, 8, affectsDisplay)]);

    [Fact]
    public void Scan_FindsNothingOnACleanStart()
    {
        var service = new RecoveryService(
            new FileTransactionStore(_dir), new CountingApplier(), new TestLog());

        Assert.Empty(service.Scan());
    }

    [Fact]
    public void Scan_FindsATransactionLeftOpenByAnInterruption()
    {
        var store = new FileTransactionStore(_dir);
        store.Write(Display(affectsDisplay: true));

        var service = new RecoveryService(store, new CountingApplier(), new TestLog());

        Assert.Single(service.Scan());
    }

    [Fact]
    public void Scan_IgnoresTransactionsThatCompletedNormally()
    {
        var store = new FileTransactionStore(_dir);
        store.Write(Display(affectsDisplay: true).Commit(DateTimeOffset.UtcNow));

        var service = new RecoveryService(store, new CountingApplier(), new TestLog());

        Assert.Empty(service.Scan());
    }

    [Fact]
    public void RecoverAutomatically_RestoresDisplayAffectingTransactionsWithoutAsking()
    {
        // Une transaction interrompue qui touchait l'affichage doit etre defaite sans
        // attendre : l'utilisateur n'a peut-etre plus d'ecran pour repondre.
        var store = new FileTransactionStore(_dir);
        store.Write(Display(affectsDisplay: true));

        var applier = new CountingApplier();
        var service = new RecoveryService(store, applier, new TestLog());

        Assert.Equal(1, service.RecoverAutomatically());
        Assert.Equal(1, applier.Reverted);
        Assert.Empty(store.ReadPending());
    }

    [Fact]
    public void RecoverAutomatically_LeavesNonDisplayTransactionsForTheUserToDecide()
    {
        var store = new FileTransactionStore(_dir);
        store.Write(Display(affectsDisplay: false));

        var applier = new CountingApplier();
        var service = new RecoveryService(store, applier, new TestLog());

        Assert.Equal(0, service.RecoverAutomatically());
        Assert.Equal(0, applier.Reverted);
        Assert.Single(store.ReadPending());
    }

    [Fact]
    public void RecoverAll_RestoresEveryPendingTransaction()
    {
        var store = new FileTransactionStore(_dir);
        store.Write(Display(affectsDisplay: false));
        store.Write(Display(affectsDisplay: true));

        var service = new RecoveryService(store, new CountingApplier(), new TestLog());

        Assert.Equal(2, service.RecoverAll());
        Assert.Empty(store.ReadPending());
    }
}

/// <summary>
/// Simule une interruption brutale a chaque instant critique et verifie qu'aucun
/// scenario ne laisse une modification sans moyen de retour.
/// </summary>
public sealed class CrashRecoveryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "fc-crash-rec", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    /// <summary>Etat systeme simule : ce que l'utilisateur verrait apres redemarrage.</summary>
    private sealed class FakeSystem : IChangeApplier
    {
        public Dictionary<string, object?> Values { get; } = [];
        public int CrashAfter { get; set; } = int.MaxValue;

        private int _applied;

        public void Apply(ChangeRecord change)
        {
            if (_applied >= CrashAfter) throw new OperationCanceledException("coupure simulee");

            Values[change.ValueName!] = change.NewValue;
            _applied++;
        }

        public void Revert(ChangeRecord change)
        {
            if (change.PreviousValueExisted)
            {
                Values[change.ValueName!] = change.PreviousValue;
            }
            else
            {
                // La valeur n'existait pas : la restauration la SUPPRIME, elle n'y
                // ecrit pas zero. C'est toute la difference entre remettre le defaut
                // de Windows et imposer une valeur qui n'a jamais existe.
                Values.Remove(change.ValueName!);
            }
        }
    }

    private static Transaction Sample() => Transaction.Open(
        "FG-TDR-001", "Allonger le delai TDR", RiskLevel.Eleve,
        [
            ChangeRecord.RegistryValue(@"HKLM\Test", "TdrDelay", null, 8),
            ChangeRecord.RegistryValue(@"HKLM\Test", "TdrLevel", null, 3),
        ]);

    [Fact]
    public void ACrashMidApplyLeavesARecoverablePlanOnDisk()
    {
        var store = new FileTransactionStore(_dir);
        var system = new FakeSystem { CrashAfter = 1 };

        // Session 1 : l'application est interrompue apres le premier changement.
        // Le moteur rattrape l'exception et defait ce qui avait ete applique.
        var result = new TransactionRunner(store, system, new TestLog()).Run(Sample());
        Assert.False(result.Succeeded);

        // Session 2 : au redemarrage, plus rien ne doit rester en attente.
        var reboot = new FileTransactionStore(_dir);
        new RecoveryService(reboot, system, new TestLog()).RecoverAutomatically();

        Assert.Empty(system.Values);
        Assert.Empty(reboot.ReadPending());
    }

    [Fact]
    public void ACrashBeforeCommitIsUndoneOnTheNextStart()
    {
        var store = new FileTransactionStore(_dir);
        var system = new FakeSystem();
        var transaction = Sample();

        // Le plan est ecrit, les changements sont appliques, puis tout s'arrete avant
        // la validation : le pire moment possible. Rien ne marque la transaction close.
        store.Write(transaction);
        foreach (var change in transaction.Changes) system.Apply(change);

        Assert.Equal(2, system.Values.Count);

        var recovered = new RecoveryService(
            new FileTransactionStore(_dir), system, new TestLog()).RecoverAutomatically();

        Assert.Equal(1, recovered);
        Assert.Empty(system.Values);
    }

    [Fact]
    public void ACommittedTransactionIsNeverUndoneOnRestart()
    {
        var store = new FileTransactionStore(_dir);
        var system = new FakeSystem();

        new TransactionRunner(store, system, new TestLog()).Run(Sample());
        Assert.Equal(2, system.Values.Count);

        new RecoveryService(new FileTransactionStore(_dir), system, new TestLog()).RecoverAutomatically();

        // La modification voulue est conservee : la reprise ne doit defaire que ce
        // qui a ete interrompu.
        Assert.Equal(2, system.Values.Count);
    }

    [Fact]
    public void RecoveryRestoresAbsenceRatherThanWritingZero()
    {
        var store = new FileTransactionStore(_dir);
        var system = new FakeSystem();
        var transaction = Sample();

        store.Write(transaction);
        foreach (var change in transaction.Changes) system.Apply(change);

        new RecoveryService(new FileTransactionStore(_dir), system, new TestLog()).RecoverAutomatically();

        // Les cles doivent avoir disparu, pas valoir zero : Windows applique alors ses
        // propres defauts, ce qui est bien l'etat d'origine.
        Assert.False(system.Values.ContainsKey("TdrDelay"));
        Assert.False(system.Values.ContainsKey("TdrLevel"));
    }
}
