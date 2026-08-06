using ForgeConflict.Core.Inventory;
using ForgeConflict.Core.Logging;
using ForgeConflict.Core.Rules.Catalog;

namespace ForgeConflict.Core.Rules;

public sealed record RuleReport(
    IReadOnlyList<Finding> Findings,
    IReadOnlyList<string> FailedRules)
{
    public Severity? WorstSeverity => Findings.Count == 0 ? null : Findings.Max(f => f.Severity);

    /// <summary>Constats reellement problematiques, hors simples informations.</summary>
    public IReadOnlyList<Finding> Problems =>
        Findings.Where(f => f.Severity > Severity.Information).ToList();

    public bool IsComplete => FailedRules.Count == 0;
}

/// <summary>
/// Evalue les regles contre un instantane. Chaque regle est isolee : une regle qui
/// leve une exception est signalee et ignoree, sans empecher les autres d'aboutir.
///
/// Le moteur ne corrige rien. Il rassemble des constats.
/// </summary>
public sealed class RuleEngine
{
    private readonly IReadOnlyList<IRule> _rules;
    private readonly ILog _log;

    public RuleEngine(ILog log) : this(log, DefaultRules())
    {
    }

    public RuleEngine(ILog log, IReadOnlyList<IRule> rules)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(rules);

        _log = log;
        _rules = rules;
    }

    public static IReadOnlyList<IRule> DefaultRules() =>
    [
        new DuplicateDriverPackageRule(),
        new GenericDriverRule(),
        new UnsignedDriverRule(),
        new TdrSettingsRule(),
        new StoppedVendorServiceRule(),
        new OverlayConflictRule(),
        new CrashHistoryRule(),
        new PcieLinkRule(),
        new HybridGpuRule(),
    ];

    public RuleReport Evaluate(SystemSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var findings = new List<Finding>();
        var failed = new List<string>();

        foreach (var rule in _rules)
        {
            try
            {
                findings.AddRange(rule.Evaluate(snapshot));
            }
            catch (Exception ex)
            {
                // Une regle defaillante est signalee comme telle : taire son echec
                // laisserait croire que rien n'a ete trouve, alors que rien n'a ete
                // cherche.
                failed.Add(rule.Id);
                _log.Write(LogLevel.Error, $"regle {rule.Id} : evaluation impossible", ex);
            }
        }

        // Le plus grave en premier, puis par identifiant pour un ordre stable d'un
        // scan a l'autre.
        var ordered = findings
            .OrderByDescending(f => f.Severity)
            .ThenBy(f => f.RuleId, StringComparer.Ordinal)
            .ToList();

        _log.Write(LogLevel.Info,
            $"analyse des regles : {ordered.Count} constat(s), {failed.Count} regle(s) en echec");

        return new RuleReport(ordered, failed);
    }
}
