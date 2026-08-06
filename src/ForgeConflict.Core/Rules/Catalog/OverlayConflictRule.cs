using ForgeConflict.Core.Inventory;
using ForgeConflict.Core.Safety;

namespace ForgeConflict.Core.Rules.Catalog;

/// <summary>
/// Signale les superpositions actives qui s'accrochent au rendu. C'est la premiere
/// cause de plantage graphique d'origine applicative.
///
/// La regle constate leur presence sans les condamner : ces logiciels sont utilises
/// volontairement, et la plupart du temps sans incident.
/// </summary>
public sealed class OverlayConflictRule : IRule
{
    /// <summary>Nombre d'overlays au-dela duquel le cumul devient notable.</summary>
    private const int NoticeableCount = 3;

    public string Id => "FG-APP-001";

    public RuleCategory Category => RuleCategory.ApplicationConflict;

    public IReadOnlyList<Finding> Evaluate(SystemSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!snapshot.OverlayProcesses.IsUsable || snapshot.OverlayProcesses.Value is not { } processes)
        {
            return [];
        }

        // Les processus sont regroupes par application : un navigateur embarque
        // compte une dizaine de processus pour une seule superposition.
        var applications = processes
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (applications.Count == 0) return [];

        var crashed = HasRecentGpuCrash(snapshot);

        if (applications.Count < NoticeableCount && !crashed) return [];

        var evidence = string.Join(
            Environment.NewLine,
            applications.Select(g =>
                $"  {g.Key} — {g.Count()} processus" +
                (g.First().Company is { } company && !string.IsNullOrWhiteSpace(company)
                    ? $" ({company})"
                    : string.Empty)));

        return
        [
            new Finding(
                RuleId: Id,
                Category: Category,
                // Sans historique de plantage, la coexistence d'overlays est un simple
                // constat. Avec, elle devient une piste serieuse.
                Severity: crashed ? Severity.Eleve : Severity.Information,
                Title: $"{applications.Count} applications s'accrochent au rendu graphique",
                Evidence: evidence,
                Explanation: crashed
                    ? "Ces applications s'inserent dans le rendu pour afficher leur superposition. " +
                      "Des incidents graphiques ont ete releves sur cette machine, et le cumul de " +
                      "plusieurs superpositions en est une cause frequente : elles se genent " +
                      "mutuellement dans la chaine de rendu."
                    : "Ces applications s'inserent dans le rendu pour afficher leur superposition. " +
                      "Aucun incident n'a ete releve : leur presence est signalee a titre indicatif, " +
                      "elle ne constitue pas un probleme en soi.",
                SuggestedFix: crashed
                    ? "Desactiver les superpositions une par une, en commencant par les moins utiles, " +
                      "pour identifier celle qui provoque les incidents."
                    : null,
                FixRisk: crashed ? RiskLevel.Faible : null),
        ];
    }

    private static bool HasRecentGpuCrash(SystemSnapshot snapshot) =>
        (snapshot.GpuEvents.IsUsable && snapshot.GpuEvents.Value is { Count: > 0 }) ||
        (snapshot.CrashArtifacts.IsUsable && snapshot.CrashArtifacts.Value is { Count: > 0 });
}
