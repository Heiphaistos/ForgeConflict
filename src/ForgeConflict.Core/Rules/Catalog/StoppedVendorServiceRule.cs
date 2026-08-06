using ForgeConflict.Core.Inventory;
using ForgeConflict.Core.Inventory.Model;
using ForgeConflict.Core.Safety;

namespace ForgeConflict.Core.Rules.Catalog;

/// <summary>
/// Signale un service constructeur configure pour demarrer automatiquement mais qui
/// ne tourne pas. Un service en demarrage automatique et arrete a echoue ou a ete
/// interrompu ; un service en demarrage manuel et arrete est parfaitement normal.
/// </summary>
public sealed class StoppedVendorServiceRule : IRule
{
    public string Id => "FG-SVC-001";

    public RuleCategory Category => RuleCategory.ServiceConflict;

    public IReadOnlyList<Finding> Evaluate(SystemSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!snapshot.Services.IsUsable || snapshot.Services.Value is not { } services) return [];

        // Seul le couple « demarrage automatique » + « arrete » est anormal.
        var failed = services
            .Where(s => IsAutomatic(s) && IsStopped(s))
            .ToList();

        if (failed.Count == 0) return [];

        return
        [
            new Finding(
                RuleId: Id,
                Category: Category,
                Severity: Severity.Moyen,
                Title: $"{failed.Count} service(s) graphique(s) en echec de demarrage",
                Evidence: string.Join(
                    Environment.NewLine,
                    failed.Select(s => $"  {s.Name} [{s.RelatedVendor}] {s.State} / {s.StartMode}")),
                Explanation:
                    "Ces services sont configures pour demarrer avec Windows mais ne tournent pas. " +
                    "Ils assurent le panneau de configuration du constructeur, la gestion des profils " +
                    "par application et les fonctions d'affichage avancees. Leur arret prive de ces " +
                    "fonctions sans empecher l'affichage de base, et signale souvent une installation " +
                    "de pilote incomplete.",
                SuggestedFix: "Redemarrer ces services, ou reinstaller le pilote constructeur s'ils echouent a nouveau.",
                FixRisk: RiskLevel.Moyen),
        ];
    }

    private static bool IsAutomatic(ServiceInfo service) =>
        service.StartMode?.StartsWith("Auto", StringComparison.OrdinalIgnoreCase) ?? false;

    private static bool IsStopped(ServiceInfo service) =>
        string.Equals(service.State, "Stopped", StringComparison.OrdinalIgnoreCase);
}
