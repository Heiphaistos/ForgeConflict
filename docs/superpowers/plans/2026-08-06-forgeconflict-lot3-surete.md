# ForgeConflict — Lot 3 : couche de sûreté

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rendre impossible qu'une correction laisse la machine sans affichage, en écrivant le mécanisme de restauration **avant** toute remédiation.

**Architecture:** Trois pièces indépendantes et testables. Un journal transactionnel qui inscrit son rollback sur disque avant d'agir. Un service de reprise qui relit ce journal à chaque démarrage. Un watchdog d'affichage qui exige une confirmation humaine sous 30 secondes, sinon revient en arrière.

**Tech Stack:** .NET 8, `Microsoft.Win32.Registry`, `System.Text.Json`, xUnit.

**Référence:** `docs/superpowers/specs/2026-08-05-forgeconflict-design.md` section 6.

**Version :** 0.5.0 → 0.6.0

---

## Le principe directeur

**Le rollback est écrit avant l'action, jamais après.** Si l'ordre était inversé, une coupure entre
l'action et l'écriture du journal laisserait une modification sans moyen de retour — exactement le
scénario qu'on cherche à rendre impossible.

Une transaction connaît trois états sur disque : `Ouverte` (l'action est en cours ou a été
interrompue), `Validee` (l'action a abouti et a été confirmée), `Restauree` (l'état antérieur a été
rétabli). **Toute transaction encore `Ouverte` au démarrage signale une interruption** : plantage,
écran noir, ou coupure de courant. Elle déclenche la restauration.

**Ce lot n'écrit toujours rien dans le système.** Il fournit le mécanisme ; les remédiations qui
l'utiliseront viennent au lot 5. Les tests s'appuient sur une abstraction de dépôt en mémoire, ce
qui permet de simuler une coupure à n'importe quel instant sans toucher au registre.

---

### Task 1 : Modèle de transaction

**Files:**
- Create: `src/ForgeConflict.Core/Safety/ChangeRecord.cs`
- Create: `src/ForgeConflict.Core/Safety/Transaction.cs`
- Test: `tests/ForgeConflict.Tests/Safety/TransactionTests.cs`

- [ ] **Step 1 : Écrire les tests qui échouent**

Create `tests/ForgeConflict.Tests/Safety/TransactionTests.cs` :

```csharp
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
```

- [ ] **Step 2 : Lancer les tests pour vérifier l'échec**

Run : `dotnet test ForgeConflict.sln --filter FullyQualifiedName~TransactionTests`
Expected : échec de compilation, `Transaction` étant introuvable.

- [ ] **Step 3 : Écrire le changement**

Create `src/ForgeConflict.Core/Safety/ChangeRecord.cs` :

```csharp
namespace ForgeConflict.Core.Safety;

public enum ChangeKind
{
    RegistryValue,
    ServiceStartMode,
    ScheduledTask,
}

/// <summary>
/// Une modification unitaire, avec de quoi la defaire. La valeur precedente est
/// conservee telle quelle, y compris nulle : nulle signifie que la valeur n'existait
/// pas et que la restauration doit la supprimer, non y ecrire zero.
/// </summary>
public sealed record ChangeRecord(
    ChangeKind Kind,
    string Target,
    string? ValueName,
    object? PreviousValue,
    object? NewValue,
    bool PreviousValueExisted,
    bool AffectsDisplay)
{
    public static ChangeRecord RegistryValue(
        string keyPath,
        string valueName,
        object? previousValue,
        object? newValue,
        bool affectsDisplay = true) =>
        new(ChangeKind.RegistryValue, keyPath, valueName, previousValue, newValue,
            PreviousValueExisted: previousValue is not null,
            AffectsDisplay: affectsDisplay);

    public string Describe() => Kind switch
    {
        ChangeKind.RegistryValue => PreviousValueExisted
            ? $"{Target}\\{ValueName} : {PreviousValue} -> {NewValue}"
            : $"{Target}\\{ValueName} : absente -> {NewValue}",
        ChangeKind.ServiceStartMode => $"service {Target} : {PreviousValue} -> {NewValue}",
        _ => $"{Target} : {PreviousValue} -> {NewValue}",
    };
}
```

- [ ] **Step 4 : Écrire la transaction**

Create `src/ForgeConflict.Core/Safety/Transaction.cs` :

```csharp
namespace ForgeConflict.Core.Safety;

public enum TransactionState
{
    Ouverte,
    Validee,
    Restauree,
}

/// <summary>
/// Niveau de risque d'une ACTION, a ne pas confondre avec la severite d'un CONSTAT.
/// Voir section 6.0 du document de conception.
/// </summary>
public enum RiskLevel
{
    Faible,
    Moyen,
    Eleve,
    Critique,
}

/// <summary>
/// Unite de modification reversible. Son plan de restauration est ecrit sur disque
/// AVANT que la moindre modification soit appliquee : une transaction retrouvee
/// <see cref="TransactionState.Ouverte"/> au demarrage signale une interruption et
/// declenche le retour arriere.
/// </summary>
public sealed record Transaction(
    Guid Id,
    string RuleId,
    string Title,
    RiskLevel Risk,
    TransactionState State,
    DateTimeOffset OpenedAt,
    DateTimeOffset? ClosedAt,
    IReadOnlyList<ChangeRecord> Changes)
{
    public bool NeedsRecovery => State == TransactionState.Ouverte;

    public bool AffectsDisplay => Changes.Any(c => c.AffectsDisplay);

    public static Transaction Open(
        string ruleId,
        string title,
        RiskLevel risk,
        IReadOnlyList<ChangeRecord> changes,
        DateTimeOffset? openedAt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleId);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(changes);

        if (changes.Count == 0)
        {
            throw new ArgumentException(
                "Une transaction sans changement n'a rien a restaurer.", nameof(changes));
        }

        return new Transaction(
            Guid.NewGuid(), ruleId, title, risk, TransactionState.Ouverte,
            openedAt ?? DateTimeOffset.Now, null, changes);
    }

    public Transaction Commit(DateTimeOffset at) =>
        this with { State = TransactionState.Validee, ClosedAt = at };

    public Transaction RolledBack(DateTimeOffset at) =>
        this with { State = TransactionState.Restauree, ClosedAt = at };
}
```

- [ ] **Step 5 : Lancer les tests pour vérifier le succès**

Run : `dotnet test ForgeConflict.sln --filter FullyQualifiedName~TransactionTests`
Expected : `Failed: 0, Passed: 6`.

- [ ] **Step 6 : Commit**

```bash
git add src/ForgeConflict.Core/Safety tests/ForgeConflict.Tests/Safety
git commit -m "Add reversible transaction model with explicit undo plan"
```

---

### Task 2 : Journal transactionnel sur disque

**Files:**
- Create: `src/ForgeConflict.Core/Safety/ITransactionStore.cs`
- Create: `src/ForgeConflict.Core/Safety/FileTransactionStore.cs`
- Test: `tests/ForgeConflict.Tests/Safety/FileTransactionStoreTests.cs`

- [ ] **Step 1 : Écrire les tests qui échouent**

Create `tests/ForgeConflict.Tests/Safety/FileTransactionStoreTests.cs` :

```csharp
using ForgeConflict.Core.Safety;

namespace ForgeConflict.Tests.Safety;

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
```

- [ ] **Step 2 : Lancer les tests pour vérifier l'échec**

Run : `dotnet test ForgeConflict.sln --filter FullyQualifiedName~FileTransactionStoreTests`
Expected : échec de compilation, `FileTransactionStore` étant introuvable.

- [ ] **Step 3 : Écrire le contrat**

Create `src/ForgeConflict.Core/Safety/ITransactionStore.cs` :

```csharp
namespace ForgeConflict.Core.Safety;

/// <summary>
/// Depot des transactions. L'ecriture doit etre durable AVANT que l'action
/// correspondante soit appliquee : c'est cette garantie qui rend une interruption
/// recuperable.
/// </summary>
public interface ITransactionStore
{
    void Write(Transaction transaction);

    IReadOnlyList<Transaction> ReadAll();

    IReadOnlyList<Transaction> ReadPending();
}
```

- [ ] **Step 4 : Écrire l'implémentation**

Create `src/ForgeConflict.Core/Safety/FileTransactionStore.cs` :

```csharp
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ForgeConflict.Core.Safety;

/// <summary>
/// Journal sur disque, un fichier par transaction. Le decoupage en fichiers isole les
/// degats : une ecriture interrompue n'abime que sa propre entree, jamais les autres.
/// </summary>
public sealed class FileTransactionStore : ITransactionStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _directory;

    public FileTransactionStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory;
    }

    public void Write(Transaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        Directory.CreateDirectory(_directory);
        var path = PathFor(transaction.Id);
        var temporary = path + ".tmp";

        // Ecriture puis remplacement atomique : un fichier definitif n'est jamais
        // observe a moitie ecrit, meme si le courant tombe pendant l'operation.
        File.WriteAllText(temporary, JsonSerializer.Serialize(transaction, Options), Encoding.UTF8);
        File.Move(temporary, path, overwrite: true);
    }

    public IReadOnlyList<Transaction> ReadAll()
    {
        if (!Directory.Exists(_directory)) return [];

        var transactions = new List<Transaction>();

        foreach (var path in Directory.EnumerateFiles(_directory, "*.json"))
        {
            try
            {
                var transaction = JsonSerializer.Deserialize<Transaction>(
                    File.ReadAllText(path), Options);

                if (transaction is not null) transactions.Add(transaction);
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                // Entree tronquee par une coupure : on l'ignore plutot que d'empecher
                // la lecture des transactions saines, dont certaines attendent
                // peut-etre une restauration.
            }
        }

        return transactions;
    }

    public IReadOnlyList<Transaction> ReadPending() =>
        ReadAll().Where(t => t.NeedsRecovery).ToList();

    private string PathFor(Guid id) => Path.Combine(_directory, $"{id:N}.json");
}
```

- [ ] **Step 5 : Lancer les tests pour vérifier le succès**

Run : `dotnet test ForgeConflict.sln --filter FullyQualifiedName~FileTransactionStoreTests`
Expected : `Failed: 0, Passed: 6`.

- [ ] **Step 6 : Commit**

```bash
git add src/ForgeConflict.Core/Safety tests/ForgeConflict.Tests/Safety
git commit -m "Add file transaction store with atomic writes and corruption tolerance"
```

---

### Task 3 : Applicateur transactionnel

**Files:**
- Create: `src/ForgeConflict.Core/Safety/IChangeApplier.cs`
- Create: `src/ForgeConflict.Core/Safety/TransactionRunner.cs`
- Test: `tests/ForgeConflict.Tests/Safety/TransactionRunnerTests.cs`

- [ ] **Step 1 : Écrire les tests qui échouent**

Ces tests sont le cœur du lot. Ils simulent une coupure à chaque instant critique.

Create `tests/ForgeConflict.Tests/Safety/TransactionRunnerTests.cs` :

```csharp
using ForgeConflict.Core.Safety;

namespace ForgeConflict.Tests.Safety;

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

    private static ChangeRecord Change(string name) =>
        ChangeRecord.RegistryValue(@"HKLM\Test", name, null, 1);

    [Fact]
    public void Run_WritesTheUndoPlanBeforeApplyingAnything()
    {
        var store = new MemoryStore();
        var applier = new RecordingApplier();
        var runner = new TransactionRunner(store, applier, new TestLog());

        runner.Run(Transaction.Open("id", "titre", RiskLevel.Moyen, [Change("A")]));

        // L'entree ouverte doit precede l'entree validee : c'est la garantie qu'une
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
        var transaction = Transaction.Open("id", "titre", RiskLevel.Moyen,
            [Change("A"), Change("B"), Change("C")]);

        runner.Rollback(transaction);

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
}
```

- [ ] **Step 2 : Lancer les tests pour vérifier l'échec**

Run : `dotnet test ForgeConflict.sln --filter FullyQualifiedName~TransactionRunnerTests`
Expected : échec de compilation, `TransactionRunner` étant introuvable.

- [ ] **Step 3 : Écrire le contrat d'application**

Create `src/ForgeConflict.Core/Safety/IChangeApplier.cs` :

```csharp
namespace ForgeConflict.Core.Safety;

/// <summary>
/// Applique et defait une modification unitaire. Separe du moteur transactionnel
/// pour que la logique de sûrete soit testable sans jamais toucher au systeme.
/// </summary>
public interface IChangeApplier
{
    void Apply(ChangeRecord change);

    void Revert(ChangeRecord change);
}
```

- [ ] **Step 4 : Écrire le moteur**

Create `src/ForgeConflict.Core/Safety/TransactionRunner.cs` :

```csharp
using ForgeConflict.Core.Logging;

namespace ForgeConflict.Core.Safety;

public sealed record TransactionResult(bool Succeeded, string Message, Transaction Transaction);

/// <summary>
/// Execute une transaction en garantissant la reversibilite.
///
/// L'ordre est imperatif : le plan de restauration est ecrit AVANT la premiere
/// modification. Une coupure a n'importe quel instant laisse donc soit un etat
/// coherent, soit une transaction ouverte que la reprise au demarrage saura defaire.
/// </summary>
public sealed class TransactionRunner
{
    private readonly ITransactionStore _store;
    private readonly IChangeApplier _applier;
    private readonly ILog _log;

    public TransactionRunner(ITransactionStore store, IChangeApplier applier, ILog log)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(applier);
        ArgumentNullException.ThrowIfNull(log);

        _store = store;
        _applier = applier;
        _log = log;
    }

    public TransactionResult Run(Transaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        // 1. Le plan de restauration d'abord, sans exception.
        _store.Write(transaction);
        _log.Write(LogLevel.Info, $"transaction {transaction.Id:N} ouverte : {transaction.Title}");

        var applied = new List<ChangeRecord>();

        try
        {
            foreach (var change in transaction.Changes)
            {
                _applier.Apply(change);
                applied.Add(change);
            }
        }
        catch (Exception ex)
        {
            _log.Write(LogLevel.Error, $"transaction {transaction.Id:N} : echec, retour arriere", ex);

            RevertAll(applied);
            var restored = transaction.RolledBack(DateTimeOffset.Now);
            _store.Write(restored);

            return new TransactionResult(false, $"Echec : {ex.Message}. L'etat precedent a ete retabli.", restored);
        }

        var committed = transaction.Commit(DateTimeOffset.Now);
        _store.Write(committed);
        _log.Write(LogLevel.Info, $"transaction {transaction.Id:N} validee");

        return new TransactionResult(true, "Modification appliquee.", committed);
    }

    public TransactionResult Rollback(Transaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        var failures = RevertAll(transaction.Changes);
        var restored = transaction.RolledBack(DateTimeOffset.Now);
        _store.Write(restored);

        return failures == 0
            ? new TransactionResult(true, "Etat precedent retabli.", restored)
            : new TransactionResult(false,
                $"Etat retabli partiellement : {failures} modification(s) n'ont pas pu etre defaites.", restored);
    }

    /// <summary>
    /// Defait en ordre inverse et poursuit malgre les echecs : s'arreter a la premiere
    /// difficulte laisserait la machine dans un etat intermediaire, pire que l'etat de
    /// depart comme que l'etat cible.
    /// </summary>
    private int RevertAll(IReadOnlyList<ChangeRecord> changes)
    {
        var failures = 0;

        for (var i = changes.Count - 1; i >= 0; i--)
        {
            try
            {
                _applier.Revert(changes[i]);
            }
            catch (Exception ex)
            {
                failures++;
                _log.Write(LogLevel.Error, $"restauration impossible : {changes[i].Describe()}", ex);
            }
        }

        return failures;
    }
}
```

- [ ] **Step 5 : Lancer les tests pour vérifier le succès**

Run : `dotnet test ForgeConflict.sln --filter FullyQualifiedName~TransactionRunnerTests`
Expected : `Failed: 0, Passed: 6`.

- [ ] **Step 6 : Commit**

```bash
git add src/ForgeConflict.Core/Safety tests/ForgeConflict.Tests/Safety
git commit -m "Add transaction runner writing the undo plan before any change"
```

---

### Task 4 : Reprise au démarrage

**Files:**
- Create: `src/ForgeConflict.Core/Safety/RecoveryService.cs`
- Test: `tests/ForgeConflict.Tests/Safety/RecoveryServiceTests.cs`

- [ ] **Step 1 : Écrire les tests qui échouent**

Create `tests/ForgeConflict.Tests/Safety/RecoveryServiceTests.cs` :

```csharp
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
        var store = new FileTransactionStore(_dir);
        var service = new RecoveryService(store, new CountingApplier(), new TestLog());

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

        var recovered = service.RecoverAutomatically();

        Assert.Equal(1, recovered);
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

        var applier = new CountingApplier();
        var service = new RecoveryService(store, applier, new TestLog());

        Assert.Equal(2, service.RecoverAll());
        Assert.Empty(store.ReadPending());
    }
}
```

- [ ] **Step 2 : Lancer les tests pour vérifier l'échec**

Run : `dotnet test ForgeConflict.sln --filter FullyQualifiedName~RecoveryServiceTests`
Expected : échec de compilation, `RecoveryService` étant introuvable.

- [ ] **Step 3 : Écrire le service**

Create `src/ForgeConflict.Core/Safety/RecoveryService.cs` :

```csharp
using ForgeConflict.Core.Logging;

namespace ForgeConflict.Core.Safety;

/// <summary>
/// Relit le journal a chaque demarrage. Une transaction encore ouverte signale une
/// interruption : plantage, ecran noir ou coupure de courant pendant l'application.
///
/// Celles qui touchaient l'affichage sont defaites AUTOMATIQUEMENT : si l'ecran est
/// noir, l'utilisateur n'a aucun moyen de repondre a une question. Les autres lui
/// sont presentees, car rien n'urge et le choix lui revient.
/// </summary>
public sealed class RecoveryService
{
    private readonly ITransactionStore _store;
    private readonly IChangeApplier _applier;
    private readonly ILog _log;

    public RecoveryService(ITransactionStore store, IChangeApplier applier, ILog log)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(applier);
        ArgumentNullException.ThrowIfNull(log);

        _store = store;
        _applier = applier;
        _log = log;
    }

    public IReadOnlyList<Transaction> Scan()
    {
        var pending = _store.ReadPending();

        if (pending.Count > 0)
        {
            _log.Write(LogLevel.Warn,
                $"{pending.Count} transaction(s) interrompue(s) trouvee(s) au demarrage");
        }

        return pending;
    }

    /// <summary>Defait sans demander les transactions qui touchaient l'affichage.</summary>
    public int RecoverAutomatically() => Recover(t => t.AffectsDisplay);

    /// <summary>Defait toutes les transactions en attente, sur decision de l'utilisateur.</summary>
    public int RecoverAll() => Recover(_ => true);

    private int Recover(Func<Transaction, bool> predicate)
    {
        var runner = new TransactionRunner(_store, _applier, _log);
        var count = 0;

        foreach (var transaction in Scan().Where(predicate))
        {
            _log.Write(LogLevel.Warn,
                $"restauration de la transaction interrompue {transaction.Id:N} : {transaction.Title}");

            runner.Rollback(transaction);
            count++;
        }

        return count;
    }
}
```

- [ ] **Step 4 : Lancer les tests pour vérifier le succès**

Run : `dotnet test ForgeConflict.sln --filter FullyQualifiedName~RecoveryServiceTests`
Expected : `Failed: 0, Passed: 6`.

- [ ] **Step 5 : Commit**

```bash
git add src/ForgeConflict.Core/Safety tests/ForgeConflict.Tests/Safety
git commit -m "Add boot recovery restoring interrupted display changes without asking"
```

---

### Task 5 : Watchdog d'affichage

**Files:**
- Create: `src/ForgeConflict.Core/Safety/DisplayWatchdog.cs`
- Test: `tests/ForgeConflict.Tests/Safety/DisplayWatchdogTests.cs`

- [ ] **Step 1 : Écrire les tests qui échouent**

Create `tests/ForgeConflict.Tests/Safety/DisplayWatchdogTests.cs` :

```csharp
using ForgeConflict.Core.Safety;

namespace ForgeConflict.Tests.Safety;

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
        var refused = Task.FromResult(false);

        Assert.Equal(WatchdogOutcome.Refuse, await watchdog.WatchAsync(TimeSpan.FromSeconds(5), refused));
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
    public void Countdown_ReportsRemainingSecondsForDisplay()
    {
        var remaining = DisplayWatchdog.Remaining(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(11));

        Assert.Equal(19, remaining);
    }

    [Fact]
    public void Countdown_NeverGoesNegative() =>
        Assert.Equal(0, DisplayWatchdog.Remaining(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(45)));
}
```

- [ ] **Step 2 : Lancer les tests pour vérifier l'échec**

Run : `dotnet test ForgeConflict.sln --filter FullyQualifiedName~DisplayWatchdogTests`
Expected : échec de compilation, `DisplayWatchdog` étant introuvable.

- [ ] **Step 3 : Écrire le watchdog**

Create `src/ForgeConflict.Core/Safety/DisplayWatchdog.cs` :

```csharp
using ForgeConflict.Core.Logging;

namespace ForgeConflict.Core.Safety;

public enum WatchdogOutcome
{
    Confirme,
    Refuse,
    DelaiDepasse,
}

/// <summary>
/// Exige une confirmation humaine apres une modification touchant l'affichage.
/// Sans reponse dans le delai, le changement est annule.
///
/// C'est le mecanisme que Windows emploie lors d'un changement de resolution, et pour
/// la meme raison : si l'ecran devient noir, personne ne peut cliquer sur « Conserver ».
/// L'absence de reponse doit donc valoir refus, jamais accord.
/// </summary>
public sealed class DisplayWatchdog
{
    public static readonly TimeSpan DefaultDelay = TimeSpan.FromSeconds(30);

    private readonly ILog _log;

    public DisplayWatchdog(ILog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        _log = log;
    }

    public async Task<WatchdogOutcome> WatchAsync(TimeSpan delay, Task<bool> confirmation)
    {
        ArgumentNullException.ThrowIfNull(confirmation);

        var finished = await Task.WhenAny(confirmation, Task.Delay(delay)).ConfigureAwait(false);

        if (!ReferenceEquals(finished, confirmation))
        {
            // On cesse d'attendre la reponse : elle ne viendra peut-etre jamais.
            _log.Write(LogLevel.Warn,
                $"aucune confirmation apres {delay.TotalSeconds:0} s : retour arriere automatique");
            return WatchdogOutcome.DelaiDepasse;
        }

        var accepted = await confirmation.ConfigureAwait(false);
        _log.Write(LogLevel.Info, accepted ? "changement confirme" : "changement refuse");

        return accepted ? WatchdogOutcome.Confirme : WatchdogOutcome.Refuse;
    }

    /// <summary>Secondes restantes, pour l'affichage du compte a rebours.</summary>
    public static int Remaining(TimeSpan delay, TimeSpan elapsed) =>
        Math.Max(0, (int)Math.Ceiling((delay - elapsed).TotalSeconds));
}
```

- [ ] **Step 4 : Lancer les tests pour vérifier le succès**

Run : `dotnet test ForgeConflict.sln --filter FullyQualifiedName~DisplayWatchdogTests`
Expected : `Failed: 0, Passed: 6`.

- [ ] **Step 5 : Commit**

```bash
git add src/ForgeConflict.Core/Safety tests/ForgeConflict.Tests/Safety
git commit -m "Add display watchdog treating no answer as refusal"
```

---

### Task 6 : Test de coupure de bout en bout

**Files:**
- Create: `tests/ForgeConflict.Tests/Safety/CrashRecoveryTests.cs`

Ce test est le seul qui prouve réellement la promesse du lot. Il simule une coupure au moment le
plus dangereux et vérifie que l'état est récupérable.

- [ ] **Step 1 : Écrire le test**

Create `tests/ForgeConflict.Tests/Safety/CrashRecoveryTests.cs` :

```csharp
using ForgeConflict.Core.Safety;

namespace ForgeConflict.Tests.Safety;

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
                // La valeur n'existait pas : la restauration la SUPPRIME, elle
                // n'y ecrit pas zero. C'est toute la difference entre remettre
                // le defaut de Windows et imposer une valeur qui n'a jamais existe.
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
        try
        {
            new TransactionRunner(store, system, new TestLog()).Run(Sample());
        }
        catch (OperationCanceledException)
        {
        }

        // Session 2 : au redemarrage, le plan est retrouve et l'etat rétabli.
        var reboot = new FileTransactionStore(_dir);
        var recovery = new RecoveryService(reboot, system, new TestLog());

        recovery.RecoverAutomatically();

        Assert.Empty(system.Values);
        Assert.Empty(reboot.ReadPending());
    }

    [Fact]
    public void ACrashBeforeCommitIsUndoneOnTheNextStart()
    {
        var store = new FileTransactionStore(_dir);
        var system = new FakeSystem();
        var transaction = Sample();

        // Le plan est ecrit, les changements sont appliques, puis tout s'arrete
        // avant la validation : le pire moment possible.
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
        var transaction = Sample();

        new TransactionRunner(store, system, new TestLog()).Run(transaction);
        Assert.Equal(2, system.Values.Count);

        new RecoveryService(new FileTransactionStore(_dir), system, new TestLog()).RecoverAutomatically();

        // La modification voulue est conservee : la reprise ne doit defaire que
        // ce qui a ete interrompu.
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

        // Les cles doivent avoir disparu, pas valoir zero : Windows applique alors
        // ses propres defauts, ce qui est l'etat d'origine.
        Assert.False(system.Values.ContainsKey("TdrDelay"));
        Assert.False(system.Values.ContainsKey("TdrLevel"));
    }
}
```

- [ ] **Step 2 : Lancer les tests**

Run : `dotnet test ForgeConflict.sln --filter FullyQualifiedName~CrashRecoveryTests`
Expected : `Failed: 0, Passed: 4`.

- [ ] **Step 3 : Lancer toute la suite**

Run : `dotnet test ForgeConflict.sln --nologo`
Expected : `Failed: 0`.

- [ ] **Step 4 : Commit**

```bash
git add tests/ForgeConflict.Tests/Safety
git commit -m "Add end-to-end crash recovery tests simulating interruption mid-apply"
```

---

### Task 7 : Intégration et publication

**Files:**
- Modify: `src/ForgeConflict.App/AppPaths.cs`
- Modify: `src/ForgeConflict.App/App.xaml.cs`
- Modify: `Directory.Build.props`

- [ ] **Step 1 : Ajouter le chemin du journal transactionnel**

Modify `src/ForgeConflict.App/AppPaths.cs` — ajouter :

```csharp
    /// <summary>
    /// Le journal transactionnel vit dans ProgramData et non a cote de l'executable :
    /// il doit survivre au deplacement, au renommage ou a la suppression de l'exe
    /// portable, sans quoi une machine modifiee perdrait son moyen de retour.
    /// </summary>
    public static string JournalDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "ForgeConflict", "journal");
```

- [ ] **Step 2 : Vérifier la reprise au démarrage**

Modify `src/ForgeConflict.App/App.xaml.cs` — dans `OnStartup`, après l'application du thème :

```csharp
        // La reprise passe AVANT l'affichage de la fenetre : une transaction
        // interrompue qui touchait l'affichage doit etre defaite au plus tot.
        var store = new FileTransactionStore(AppPaths.JournalDirectory);
        var pending = new RecoveryService(store, new RegistryChangeApplier(_log), _log).Scan();
        if (pending.Count > 0)
        {
            _log.Write(LogLevel.Warn, $"{pending.Count} transaction(s) en attente de decision");
        }
```

Note : `RegistryChangeApplier` n'existe pas encore et arrive au lot 5. Pour ce lot, se contenter de
`Scan()` avec un applicateur neutre, ou reporter ce câblage — l'important est que le mécanisme et
ses tests existent.

- [ ] **Step 3 : Passer la version à 0.6.0**

Modify `Directory.Build.props` : `<Version>0.6.0</Version>`

- [ ] **Step 4 : Publier et vérifier**

Run : `powershell -ExecutionPolicy Bypass -File .\build.ps1`
Expected : `ForgeConflict 0.6.0`, `0 fichier(s) annexe(s)`.

Lancer l'application, vérifier qu'aucune transaction n'est signalée sur une machine saine et
qu'aucune exception n'apparaît dans `.logs/forgeconflict.log`.

- [ ] **Step 5 : Commit et push**

```bash
git add -A
git commit -m "Wire the safety layer into startup; bump to 0.6.0"
git push origin lot1a-socle
```

---

## Ce que ce lot ne fait pas

Aucune écriture système réelle. `IChangeApplier` n'a pas encore d'implémentation qui touche au
registre : elle vient au lot 5, avec les premières remédiations, et s'appuiera sur ce mécanisme.

Le garde-fou du mode sans échec (`bcdedit /deletevalue safeboot` inscrit avant d'y entrer) relève
du lot 10, le désinstalleur. Il suit le même principe : le moyen de sortie est armé avant l'entrée.
