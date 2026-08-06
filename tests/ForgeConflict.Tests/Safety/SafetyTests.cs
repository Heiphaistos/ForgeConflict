using ForgeConflict.Core.Safety;

namespace ForgeConflict.Tests.Safety;

public sealed class TransactionTests
{
    private static ChangeRecord Change() => ChangeRecord.RegistryValue(
        keyPath: @"HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers",
        valueName: "TdrDelay",
        previousValue: null,
        newValue: 8);

    [Fact]
    public void Open_StartsWithAnUndoPlanAndNoOutcome()
    {
        var transaction = Transaction.Open("FG-TDR-001", "Allonger le delai TDR", RiskLevel.Eleve, [Change()]);

        Assert.Equal(TransactionState.Ouverte, transaction.State);
        Assert.NotEqual(Guid.Empty, transaction.Id);
        Assert.Single(transaction.Changes);
        Assert.Null(transaction.ClosedAt);
    }

    [Fact]
    public void Commit_MarksTheTransactionValidated()
    {
        var committed = Transaction
            .Open("FG-TDR-001", "Allonger le delai TDR", RiskLevel.Eleve, [Change()])
            .Commit(DateTimeOffset.UnixEpoch);

        Assert.Equal(TransactionState.Validee, committed.State);
        Assert.Equal(DateTimeOffset.UnixEpoch, committed.ClosedAt);
    }

    [Fact]
    public void RolledBack_MarksTheTransactionRestored()
    {
        var restored = Transaction
            .Open("FG-TDR-001", "Allonger le delai TDR", RiskLevel.Eleve, [Change()])
            .RolledBack(DateTimeOffset.UnixEpoch);

        Assert.Equal(TransactionState.Restauree, restored.State);
    }

    [Fact]
    public void NeedsRecovery_IsTrueOnlyWhileTheTransactionIsStillOpen()
    {
        var open = Transaction.Open("id", "titre", RiskLevel.Faible, [Change()]);

        Assert.True(open.NeedsRecovery);
        Assert.False(open.Commit(DateTimeOffset.UtcNow).NeedsRecovery);
        Assert.False(open.RolledBack(DateTimeOffset.UtcNow).NeedsRecovery);
    }

    [Fact]
    public void Open_RejectsAnEmptyChangeSet()
    {
        // Une transaction sans changement n'a rien a restaurer : l'autoriser
        // laisserait croire qu'un retour arriere est possible alors qu'il est vide.
        Assert.Throws<ArgumentException>(() =>
            Transaction.Open("id", "titre", RiskLevel.Faible, []));
    }

    [Fact]
    public void AffectsDisplay_IsTrueWhenAnyChangeTouchesTheDisplay()
    {
        var benign = Transaction.Open("id", "titre", RiskLevel.Faible, [Change() with { AffectsDisplay = false }]);
        var risky = Transaction.Open("id", "titre", RiskLevel.Eleve, [Change() with { AffectsDisplay = true }]);

        Assert.False(benign.AffectsDisplay);
        Assert.True(risky.AffectsDisplay);
    }
}

public sealed class FileTransactionStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "fc-journal", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static Transaction Sample() => Transaction.Open(
        "FG-TDR-001", "Allonger le delai TDR", RiskLevel.Eleve,
        [ChangeRecord.RegistryValue(@"HKLM\SYSTEM\...\GraphicsDrivers", "TdrDelay", null, 8)]);

    [Fact]
    public void Write_MakesTheTransactionReadableImmediately()
    {
        var store = new FileTransactionStore(_dir);
        var transaction = Sample();

        store.Write(transaction);

        var reloaded = store.ReadAll().Single();
        Assert.Equal(transaction.Id, reloaded.Id);
        Assert.Equal(TransactionState.Ouverte, reloaded.State);
        Assert.Single(reloaded.Changes);
    }

    [Fact]
    public void Write_PreservesTheDistinctionBetweenAbsentAndZero()
    {
        // Une valeur precedente absente doit le rester apres un aller-retour :
        // la relire comme zero ferait ecrire 0 au lieu de supprimer la valeur.
        var store = new FileTransactionStore(_dir);
        store.Write(Sample());

        var change = store.ReadAll().Single().Changes.Single();
        Assert.False(change.PreviousValueExisted);
        Assert.Null(change.PreviousValue);
    }

    [Fact]
    public void Write_PreservesTheRuntimeTypeOfRegistryValues()
    {
        // Sans conservation du type, une valeur relue apres redemarrage devient un
        // JsonElement : la restauration ecrirait alors un objet inexploitable dans
        // le registre et le mecanisme de reprise serait inoperant.
        var store = new FileTransactionStore(_dir);
        var transaction = Transaction.Open("FG-TYPE", "Types", RiskLevel.Eleve,
        [
            ChangeRecord.RegistryValue(@"HKLM\Test", "Entier", 2, 8),
            ChangeRecord.RegistryValue(@"HKLM\Test", "Texte", "avant", "apres"),
        ]);

        store.Write(transaction);
        var changes = store.ReadAll().Single().Changes;

        Assert.IsType<int>(changes[0].PreviousValue);
        Assert.Equal(2, changes[0].PreviousValue);
        Assert.IsType<int>(changes[0].NewValue);
        Assert.Equal(8, changes[0].NewValue);

        Assert.IsType<string>(changes[1].PreviousValue);
        Assert.Equal("avant", changes[1].PreviousValue);
    }

    [Fact]
    public void Write_KeepsANullNewValueAsNullRatherThanAnEmptyObject()
    {
        // NewValue nulle signifie « supprimer la valeur ». La relire autrement
        // transformerait une suppression en ecriture.
        var store = new FileTransactionStore(_dir);
        var transaction = Transaction.Open("FG-DEL", "Suppression", RiskLevel.Eleve,
        [
            new ChangeRecord(ChangeKind.RegistryValue, @"HKLM\Test", "TdrLevel", 0, null, true, true),
        ]);

        store.Write(transaction);
        var change = store.ReadAll().Single().Changes.Single();

        Assert.Null(change.NewValue);
        Assert.Equal(0, change.PreviousValue);
    }

    [Fact]
    public void Write_OverwritesTheSameTransactionRatherThanDuplicating()
    {
        var store = new FileTransactionStore(_dir);
        var transaction = Sample();

        store.Write(transaction);
        store.Write(transaction.Commit(DateTimeOffset.UtcNow));

        var all = store.ReadAll();
        Assert.Single(all);
        Assert.Equal(TransactionState.Validee, all[0].State);
    }

    [Fact]
    public void ReadPending_ReturnsOnlyTransactionsThatWereNeverClosed()
    {
        var store = new FileTransactionStore(_dir);
        var interrupted = Sample();
        var finished = Sample();

        store.Write(interrupted);
        store.Write(finished.Commit(DateTimeOffset.UtcNow));

        var pending = store.ReadPending();
        Assert.Single(pending);
        Assert.Equal(interrupted.Id, pending[0].Id);
    }

    [Fact]
    public void ReadAll_SkipsCorruptedEntriesInsteadOfFailing()
    {
        // Une coupure de courant en pleine ecriture laisse un fichier tronque.
        // Il ne doit pas empecher de lire les transactions saines qui, elles,
        // ont peut-etre besoin d'etre restaurees.
        var store = new FileTransactionStore(_dir);
        store.Write(Sample());
        File.WriteAllText(Path.Combine(_dir, "tronquee.json"), "{ \"Id\": \"incomp");

        Assert.Single(store.ReadAll());
    }

    [Fact]
    public void ReadAll_ReturnsEmptyWhenNothingWasEverWritten()
    {
        var store = new FileTransactionStore(Path.Combine(_dir, "jamais-cree"));

        Assert.Empty(store.ReadAll());
        Assert.Empty(store.ReadPending());
    }
}

public sealed class TransactionRunnerTests
{
    /// <summary>Depot en memoire, pour observer l'ordre exact des ecritures.</summary>
    private sealed class MemoryStore : ITransactionStore
    {
        private readonly Dictionary<Guid, Transaction> _items = [];

        public List<TransactionState> WriteOrder { get; } = [];

        public void Write(Transaction transaction)
        {
            _items[transaction.Id] = transaction;
            WriteOrder.Add(transaction.State);
        }

        public IReadOnlyList<Transaction> ReadAll() => _items.Values.ToList();

        public IReadOnlyList<Transaction> ReadPending() =>
            _items.Values.Where(t => t.NeedsRecovery).ToList();
    }

    private sealed class RecordingApplier : IChangeApplier
    {
        public List<string> Applied { get; } = [];
        public List<string> Reverted { get; } = [];
        public Func<ChangeRecord, bool>? FailOn { get; set; }

        public void Apply(ChangeRecord change)
        {
            if (FailOn?.Invoke(change) == true)
            {
                throw new InvalidOperationException("echec simule");
            }

            Applied.Add(change.ValueName ?? change.Target);
        }

        public void Revert(ChangeRecord change) => Reverted.Add(change.ValueName ?? change.Target);
    }

    private sealed class ThrowingReverter : IChangeApplier
    {
        public int RevertedCount { get; private set; }

        public void Apply(ChangeRecord change) { }

        public void Revert(ChangeRecord change)
        {
            if (change.ValueName == "B") throw new InvalidOperationException("irreversible");
            RevertedCount++;
        }
    }

    private static ChangeRecord Change(string name) =>
        ChangeRecord.RegistryValue(@"HKLM\Test", name, null, 1);

    [Fact]
    public void Run_WritesTheUndoPlanBeforeApplyingAnything()
    {
        var store = new MemoryStore();
        var runner = new TransactionRunner(store, new RecordingApplier(), new TestLog());

        runner.Run(Transaction.Open("id", "titre", RiskLevel.Moyen, [Change("A")]));

        // L'entree ouverte doit preceder l'entree validee : c'est la garantie qu'une
        // coupure entre les deux laisse un plan de restauration exploitable.
        Assert.Equal(TransactionState.Ouverte, store.WriteOrder[0]);
        Assert.Equal(TransactionState.Validee, store.WriteOrder[^1]);
    }

    [Fact]
    public void Run_AppliesEveryChangeInOrder()
    {
        var applier = new RecordingApplier();
        var runner = new TransactionRunner(new MemoryStore(), applier, new TestLog());

        runner.Run(Transaction.Open("id", "titre", RiskLevel.Moyen, [Change("A"), Change("B")]));

        Assert.Equal(["A", "B"], applier.Applied);
        Assert.Empty(applier.Reverted);
    }

    [Fact]
    public void Run_RevertsAlreadyAppliedChangesWhenOneFails()
    {
        var applier = new RecordingApplier { FailOn = c => c.ValueName == "B" };
        var runner = new TransactionRunner(new MemoryStore(), applier, new TestLog());

        var result = runner.Run(Transaction.Open("id", "titre", RiskLevel.Moyen,
            [Change("A"), Change("B"), Change("C")]));

        Assert.False(result.Succeeded);
        Assert.Equal(["A"], applier.Applied);
        // A seul est defait : B a echoue sans rien changer, C n'a jamais ete tente.
        Assert.Equal(["A"], applier.Reverted);
    }

    [Fact]
    public void Run_MarksTheTransactionRestoredAfterAFailedApply()
    {
        var store = new MemoryStore();
        var applier = new RecordingApplier { FailOn = _ => true };
        var runner = new TransactionRunner(store, applier, new TestLog());

        runner.Run(Transaction.Open("id", "titre", RiskLevel.Moyen, [Change("A")]));

        Assert.Equal(TransactionState.Restauree, store.ReadAll().Single().State);
        Assert.Empty(store.ReadPending());
    }

    [Fact]
    public void Rollback_UndoesChangesInReverseOrder()
    {
        var applier = new RecordingApplier();
        var runner = new TransactionRunner(new MemoryStore(), applier, new TestLog());

        runner.Rollback(Transaction.Open("id", "titre", RiskLevel.Moyen,
            [Change("A"), Change("B"), Change("C")]));

        // Ordre inverse : une modification posterieure peut dependre d'une anterieure.
        Assert.Equal(["C", "B", "A"], applier.Reverted);
    }

    [Fact]
    public void Rollback_ContinuesEvenIfOneChangeCannotBeUndone()
    {
        // Abandonner a la premiere difficulte laisserait la machine dans un etat
        // intermediaire, pire que l'etat de depart comme que l'etat cible.
        var applier = new ThrowingReverter();
        var runner = new TransactionRunner(new MemoryStore(), applier, new TestLog());

        var result = runner.Rollback(Transaction.Open("id", "titre", RiskLevel.Moyen,
            [Change("A"), Change("B"), Change("C")]));

        Assert.Equal(2, applier.RevertedCount);
        Assert.False(result.Succeeded);
    }
}

public sealed class DisplayWatchdogTests
{
    [Fact]
    public async Task WatchAsync_KeepsChangesWhenConfirmedInTime()
    {
        var watchdog = new DisplayWatchdog(new TestLog());
        var confirmed = new TaskCompletionSource<bool>();

        var watching = watchdog.WatchAsync(TimeSpan.FromSeconds(5), confirmed.Task);
        confirmed.SetResult(true);

        Assert.Equal(WatchdogOutcome.Confirme, await watching);
    }

    [Fact]
    public async Task WatchAsync_RollsBackWhenNobodyConfirms()
    {
        // Personne ne repond : c'est le scenario de l'ecran noir. Le retour arriere
        // doit se declencher seul.
        var watchdog = new DisplayWatchdog(new TestLog());
        var never = new TaskCompletionSource<bool>();

        var outcome = await watchdog.WatchAsync(TimeSpan.FromMilliseconds(200), never.Task);

        Assert.Equal(WatchdogOutcome.DelaiDepasse, outcome);
    }

    [Fact]
    public async Task WatchAsync_RollsBackWhenTheUserRefuses()
    {
        var watchdog = new DisplayWatchdog(new TestLog());

        Assert.Equal(WatchdogOutcome.Refuse,
            await watchdog.WatchAsync(TimeSpan.FromSeconds(5), Task.FromResult(false)));
    }

    [Fact]
    public async Task WatchAsync_ReturnsPromptlyOnTimeoutWithoutWaitingForTheAnswer()
    {
        var watchdog = new DisplayWatchdog(new TestLog());
        var never = new TaskCompletionSource<bool>();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        await watchdog.WatchAsync(TimeSpan.FromMilliseconds(200), never.Task);
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(3),
            $"le watchdog a mis {stopwatch.Elapsed} a rendre la main");
    }

    [Fact]
    public void Countdown_ReportsRemainingSecondsForDisplay() =>
        Assert.Equal(19, DisplayWatchdog.Remaining(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(11)));

    [Fact]
    public void Countdown_NeverGoesNegative() =>
        Assert.Equal(0, DisplayWatchdog.Remaining(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(45)));
}
