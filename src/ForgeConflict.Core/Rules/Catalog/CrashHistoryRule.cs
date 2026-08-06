using System.Globalization;
using ForgeConflict.Core.Inventory;
using ForgeConflict.Core.Safety;

namespace ForgeConflict.Core.Rules.Catalog;

/// <summary>
/// Reunit l'historique d'incidents graphiques : evenements du journal Windows et
/// traces de plantage noyau laissees sur le disque.
/// </summary>
public sealed class CrashHistoryRule : IRule
{
    /// <summary>Au-dela de ce nombre de reinitialisations, le probleme est recurrent.</summary>
    private const int RecurrentThreshold = 3;

    public string Id => "FG-CRASH-001";

    public RuleCategory Category => RuleCategory.CrashHistory;

    public IReadOnlyList<Finding> Evaluate(SystemSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var findings = new List<Finding>();

        if (snapshot.GpuEvents.IsUsable && snapshot.GpuEvents.Value is { Count: > 0 } events)
        {
            var recoveries = events.Where(e => e.IsTimeoutRecovery).ToList();

            if (recoveries.Count > 0)
            {
                var recurrent = recoveries.Count >= RecurrentThreshold;
                var latest = recoveries.Max(e => e.TimeCreated);

                findings.Add(new Finding(
                    RuleId: Id,
                    Category: Category,
                    Severity: recurrent ? Severity.Eleve : Severity.Moyen,
                    Title: $"{recoveries.Count} reinitialisation(s) du pilote graphique",
                    Evidence: string.Join(
                        Environment.NewLine,
                        recoveries.Take(5).Select(e => string.Create(CultureInfo.CurrentCulture,
                            $"  {e.TimeCreated:g} — {e.ProviderName} evenement {e.EventId}"))),
                    Explanation:
                        $"Windows a du reinitialiser le pilote graphique {recoveries.Count} fois, " +
                        $"la derniere le {latest.ToString("g", CultureInfo.CurrentCulture)}. " +
                        "Chaque reinitialisation se traduit par un ecran noir de quelques secondes. " +
                        (recurrent
                            ? "La repetition indique un probleme persistant : pilote instable, " +
                              "surchauffe, alimentation insuffisante, ou memoire graphique defaillante."
                            : "Un incident isole peut n'etre qu'un accident sans suite."),
                    SuggestedFix: recurrent
                        ? "Verifier la version du pilote, les temperatures et la stabilite de l'alimentation."
                        : null,
                    FixRisk: recurrent ? RiskLevel.Moyen : null));
            }
        }

        if (snapshot.CrashArtifacts.IsUsable && snapshot.CrashArtifacts.Value is { Count: > 0 } artifacts)
        {
            findings.Add(new Finding(
                RuleId: Id,
                Category: Category,
                Severity: Severity.Eleve,
                Title: $"{artifacts.Count} trace(s) de plantage noyau presente(s)",
                Evidence: string.Join(
                    Environment.NewLine,
                    artifacts.Take(5).Select(a => string.Create(CultureInfo.CurrentCulture,
                        $"  {a.CreatedAt:d} — [{a.Kind}] {a.Category ?? "-"} {a.FileName}"))),
                Explanation:
                    "Windows a enregistre des rapports de plantage au niveau du noyau. Ces fichiers " +
                    "sont produits lorsqu'un pilote provoque une erreur grave, generalement suivie " +
                    "d'un ecran bleu ou d'un blocage complet de l'affichage. Leur presence signale " +
                    "que la machine a deja connu des defaillances serieuses liees au materiel graphique.",
                SuggestedFix: "Conserver ces fichiers pour analyse et verifier la stabilite du pilote installe.",
                FixRisk: RiskLevel.Faible));
        }

        return findings;
    }
}
