using ForgeConflict.Core.Remediation;
using ForgeConflict.Core.Rules;
using ForgeConflict.Core.Safety;
using ForgeConflict.Tests.Rules;

namespace ForgeConflict.Tests.Remediation;

public sealed class RestoreTdrDefaultsRemediationTests
{
    [Fact]
    public void Preview_ListsNothingWhenEverySettingIsAlreadyDefault()
    {
        // Machine saine : les valeurs n'existent pas, il n'y a rien a retirer.
        var preview = new RestoreTdrDefaultsRemediation().Preview(new SnapshotBuilder().Build());

        Assert.Empty(preview);
    }

    [Fact]
    public void Preview_OnlyTouchesValuesThatActuallyExist()
    {
        var snapshot = new SnapshotBuilder().WithSetting("TdrLevel", 0).Build();

        var change = Assert.Single(new RestoreTdrDefaultsRemediation().Preview(snapshot));

        Assert.Equal("TdrLevel", change.ValueName);
        Assert.Equal(0, change.PreviousValue);
        Assert.True(change.PreviousValueExisted);
    }

    [Fact]
    public void Preview_RemovesRatherThanWritesTheDocumentedDefault()
    {
        // Ecrire « 3 » figerait ce choix ; supprimer laisse Windows appliquer son
        // defaut, y compris s'il evolue avec une mise a jour.
        var snapshot = new SnapshotBuilder().WithSetting("TdrLevel", 0).Build();

        var change = Assert.Single(new RestoreTdrDefaultsRemediation().Preview(snapshot));

        Assert.Null(change.NewValue);
    }

    [Fact]
    public void Preview_MarksTheChangeAsAffectingTheDisplay()
    {
        var snapshot = new SnapshotBuilder().WithSetting("TdrDelay", 60).Build();

        Assert.All(new RestoreTdrDefaultsRemediation().Preview(snapshot),
            c => Assert.True(c.AffectsDisplay));
    }

    [Fact]
    public void Handles_OnlyMatchesItsOwnRule()
    {
        var remediation = new RestoreTdrDefaultsRemediation();

        Assert.True(remediation.Handles(Finding("FG-TDR-001")));
        Assert.False(remediation.Handles(Finding("FG-DRV-001")));
    }

    private static Finding Finding(string ruleId) =>
        new(ruleId, RuleCategory.DriverConflict, Severity.Eleve, "t", "p", "e", null, null);
}

public sealed class RemediationServiceTests
{
    private sealed class MemoryStore : ITransactionStore
    {
        private readonly Dictionary<Guid, Transaction> _items = [];

        public void Write(Transaction transaction) => _items[transaction.Id] = transaction;

        public IReadOnlyList<Transaction> ReadAll() => _items.Values.ToList();

        public IReadOnlyList<Transaction> ReadPending() =>
            _items.Values.Where(t => t.NeedsRecovery).ToList();
    }

    private sealed class SpyApplier : IChangeApplier
    {
        public int Applied { get; private set; }
        public int Reverted { get; private set; }
        public bool ShouldFail { get; set; }

        public void Apply(ChangeRecord change)
        {
            if (ShouldFail) throw new InvalidOperationException("acces refuse");
            Applied++;
        }

        public void Revert(ChangeRecord change) => Reverted++;
    }

    private static RemediationService Service(SpyApplier applier, MemoryStore store) =>
        new(store, applier, new TestLog());

    private static Core.Inventory.SystemSnapshot WithTdrSet() =>
        new SnapshotBuilder().WithSetting("TdrLevel", 0).Build();

    [Fact]
    public async Task ApplyAsync_ChangesNothingWhenTheUserDeclines()
    {
        var applier = new SpyApplier();
        var store = new MemoryStore();

        var result = await Service(applier, store).ApplyAsync(
            new RestoreTdrDefaultsRemediation(), WithTdrSet(),
            confirm: _ => Task.FromResult(false));

        Assert.Equal(RemediationOutcome.Refusee, result.Outcome);
        Assert.Equal(0, applier.Applied);
        // Rien n'est meme inscrit au journal : aucune transaction n'a ete ouverte.
        Assert.Empty(store.ReadAll());
    }

    [Fact]
    public async Task ApplyAsync_ShowsTheExactChangesBeforeAsking()
    {
        IReadOnlyList<ChangeRecord>? shown = null;

        await Service(new SpyApplier(), new MemoryStore()).ApplyAsync(
            new RestoreTdrDefaultsRemediation(), WithTdrSet(),
            confirm: changes =>
            {
                shown = changes;
                return Task.FromResult(false);
            });

        Assert.NotNull(shown);
        var change = Assert.Single(shown!);
        Assert.Equal("TdrLevel", change.ValueName);
        Assert.Equal(0, change.PreviousValue);
    }

    [Fact]
    public async Task ApplyAsync_AppliesAndKeepsWhenBothConfirmationsAreGiven()
    {
        var applier = new SpyApplier();

        var result = await Service(applier, new MemoryStore()).ApplyAsync(
            new RestoreTdrDefaultsRemediation(), WithTdrSet(),
            confirm: _ => Task.FromResult(true),
            confirmDisplay: () => Task.FromResult(true));

        Assert.Equal(RemediationOutcome.Appliquee, result.Outcome);
        Assert.Equal(1, applier.Applied);
        Assert.Equal(0, applier.Reverted);
    }

    [Fact]
    public async Task ApplyAsync_RollsBackWhenTheDisplayWatchdogGetsNoAnswer()
    {
        // Scenario de l'ecran noir : l'utilisateur ne peut plus repondre.
        var applier = new SpyApplier();
        var never = new TaskCompletionSource<bool>();

        var result = await Service(applier, new MemoryStore()).ApplyAsync(
            new RestoreTdrDefaultsRemediation(), WithTdrSet(),
            confirm: _ => Task.FromResult(true),
            confirmDisplay: () => never.Task,
            watchdogDelay: TimeSpan.FromMilliseconds(200));

        Assert.Equal(RemediationOutcome.AnnuleeParWatchdog, result.Outcome);
        Assert.Equal(1, applier.Applied);
        Assert.Equal(1, applier.Reverted);
    }

    [Fact]
    public async Task ApplyAsync_LeavesNothingPendingAfterAWatchdogRollback()
    {
        var store = new MemoryStore();
        var never = new TaskCompletionSource<bool>();

        await Service(new SpyApplier(), store).ApplyAsync(
            new RestoreTdrDefaultsRemediation(), WithTdrSet(),
            confirm: _ => Task.FromResult(true),
            confirmDisplay: () => never.Task,
            watchdogDelay: TimeSpan.FromMilliseconds(200));

        Assert.Empty(store.ReadPending());
    }

    [Fact]
    public async Task ApplyAsync_RestoresTheStateWhenApplyingFails()
    {
        var applier = new SpyApplier { ShouldFail = true };

        var result = await Service(applier, new MemoryStore()).ApplyAsync(
            new RestoreTdrDefaultsRemediation(), WithTdrSet(),
            confirm: _ => Task.FromResult(true),
            confirmDisplay: () => Task.FromResult(true));

        Assert.Equal(RemediationOutcome.Echouee, result.Outcome);
        Assert.Contains("retabli", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ApplyAsync_ReportsNoEffectRatherThanPretendingToWork()
    {
        var applier = new SpyApplier();

        var result = await Service(applier, new MemoryStore()).ApplyAsync(
            new RestoreTdrDefaultsRemediation(), new SnapshotBuilder().Build(),
            confirm: _ => Task.FromResult(true));

        Assert.Equal(RemediationOutcome.SansEffet, result.Outcome);
        Assert.Equal(0, applier.Applied);
    }

    [Fact]
    public async Task ApplyAsync_NeverAsksTheDisplayQuestionWhenNothingWasConfirmed()
    {
        var asked = false;

        await Service(new SpyApplier(), new MemoryStore()).ApplyAsync(
            new RestoreTdrDefaultsRemediation(), WithTdrSet(),
            confirm: _ => Task.FromResult(false),
            confirmDisplay: () => { asked = true; return Task.FromResult(true); });

        Assert.False(asked);
    }

    [Fact]
    public void For_FindsTheRemediationMatchingAFinding()
    {
        var finding = new Finding("FG-TDR-001", RuleCategory.DriverConflict,
            Severity.Critique, "t", "p", "e", "fix", RiskLevel.Eleve);

        Assert.IsType<RestoreTdrDefaultsRemediation>(RemediationService.For(finding));
    }

    [Fact]
    public void For_ReturnsNullWhenNoRemediationExistsYet()
    {
        var finding = new Finding("FG-HW-002", RuleCategory.HardwareConfig,
            Severity.Information, "t", "p", "e", null, null);

        Assert.Null(RemediationService.For(finding));
    }
}
