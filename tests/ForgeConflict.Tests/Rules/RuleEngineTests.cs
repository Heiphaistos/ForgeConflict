using ForgeConflict.Core.Inventory;
using ForgeConflict.Core.Logging;
using ForgeConflict.Core.Rules;

namespace ForgeConflict.Tests.Rules;

public sealed class RuleEngineTests
{
    private sealed class ThrowingRule : IRule
    {
        public string Id => "FG-TEST-BOOM";

        public RuleCategory Category => RuleCategory.DriverConflict;

        public IReadOnlyList<Finding> Evaluate(SystemSnapshot snapshot) =>
            throw new InvalidOperationException("regle defaillante");
    }

    private sealed class StubRule : IRule
    {
        private readonly Finding[] _findings;

        public StubRule(string id, params Finding[] findings)
        {
            Id = id;
            _findings = findings;
        }

        public string Id { get; }

        public RuleCategory Category => RuleCategory.DriverConflict;

        public IReadOnlyList<Finding> Evaluate(SystemSnapshot snapshot) => _findings;
    }

    private static Finding Finding(string ruleId, Severity severity) =>
        new(ruleId, RuleCategory.DriverConflict, severity, "titre", "preuve", "explication", null, null);

    [Fact]
    public void Evaluate_FindsNothingOnAHealthyMachine()
    {
        var report = new RuleEngine(new TestLog()).Evaluate(new SnapshotBuilder().Build());

        Assert.Empty(report.Problems);
        Assert.True(report.IsComplete);
    }

    [Fact]
    public void Evaluate_KeepsGoingWhenOneRuleThrows()
    {
        // Une regle defaillante ne doit pas priver l'utilisateur des constats des
        // autres regles.
        var engine = new RuleEngine(new TestLog(),
        [
            new ThrowingRule(),
            new StubRule("FG-OK", Finding("FG-OK", Severity.Moyen)),
        ]);

        var report = engine.Evaluate(new SnapshotBuilder().Build());

        Assert.Single(report.Findings);
        Assert.Contains("FG-TEST-BOOM", report.FailedRules);
        Assert.False(report.IsComplete);
    }

    [Fact]
    public void Evaluate_NamesFailedRulesRatherThanHidingThem()
    {
        // Taire l'echec laisserait croire que rien n'a ete trouve, alors que rien
        // n'a ete cherche.
        var report = new RuleEngine(new TestLog(), [new ThrowingRule()])
            .Evaluate(new SnapshotBuilder().Build());

        Assert.Empty(report.Findings);
        Assert.Single(report.FailedRules);
        Assert.False(report.IsComplete);
    }

    [Fact]
    public void Evaluate_OrdersFindingsBySeverityFirst()
    {
        var engine = new RuleEngine(new TestLog(),
        [
            new StubRule("FG-A", Finding("FG-A", Severity.Information)),
            new StubRule("FG-B", Finding("FG-B", Severity.Critique)),
            new StubRule("FG-C", Finding("FG-C", Severity.Moyen)),
        ]);

        var report = engine.Evaluate(new SnapshotBuilder().Build());

        Assert.Equal(["FG-B", "FG-C", "FG-A"], report.Findings.Select(f => f.RuleId));
        Assert.Equal(Severity.Critique, report.WorstSeverity);
    }

    [Fact]
    public void Problems_ExcludePurelyInformationalFindings()
    {
        var engine = new RuleEngine(new TestLog(),
        [
            new StubRule("FG-A", Finding("FG-A", Severity.Information)),
            new StubRule("FG-B", Finding("FG-B", Severity.Eleve)),
        ]);

        var report = engine.Evaluate(new SnapshotBuilder().Build());

        Assert.Equal(2, report.Findings.Count);
        Assert.Single(report.Problems);
    }

    [Fact]
    public void WorstSeverity_IsNullWhenNothingWasFound()
    {
        var report = new RuleEngine(new TestLog(), []).Evaluate(new SnapshotBuilder().Build());

        Assert.Null(report.WorstSeverity);
    }

    [Fact]
    public void DefaultRules_CoverEveryCategoryOfTheSpecification()
    {
        var categories = RuleEngine.DefaultRules().Select(r => r.Category).Distinct().ToList();

        Assert.Contains(RuleCategory.DriverConflict, categories);
        Assert.Contains(RuleCategory.ServiceConflict, categories);
        Assert.Contains(RuleCategory.ApplicationConflict, categories);
        Assert.Contains(RuleCategory.HardwareConfig, categories);
        Assert.Contains(RuleCategory.CrashHistory, categories);
    }

    [Fact]
    public void DefaultRules_HaveUniqueIdentifiers()
    {
        var rules = RuleEngine.DefaultRules();

        Assert.Equal(
            rules.Select(r => r.Id).Distinct(StringComparer.Ordinal).Count(),
            rules.Select(r => r.Id).Count(id => true));
    }

    [Fact]
    public void EveryRule_SurvivesASnapshotWhereEverySourceFailed()
    {
        // Aucune regle ne doit lever quand les sources sont indisponibles : c'est
        // exactement la situation d'une machine en difficulte.
        var snapshot = new SnapshotBuilder().WithUnusableGpus().WithUnusablePackages().Build();

        var report = new RuleEngine(new TestLog()).Evaluate(snapshot);

        Assert.True(report.IsComplete);
    }
}
