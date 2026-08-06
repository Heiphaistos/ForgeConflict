using ForgeConflict.Core.Inventory;

namespace ForgeConflict.Core.Rules.Catalog;

/// <summary>
/// Reconnait une configuration graphique hybride : un GPU integre au processeur
/// coexiste avec un GPU discret. Ce n'est pas un defaut, mais cette topologie
/// explique une bonne part des comportements surprenants signales par les
/// utilisateurs de portables.
/// </summary>
public sealed class HybridGpuRule : IRule
{
    public string Id => "FG-HW-002";

    public RuleCategory Category => RuleCategory.HardwareConfig;

    public IReadOnlyList<Finding> Evaluate(SystemSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!snapshot.Gpus.IsUsable || snapshot.Gpus.Value is not { } gpus) return [];

        var integrated = gpus.Where(g => g.Vendor == GpuVendor.Intel).ToList();
        var discrete = gpus.Where(g => g.Vendor is GpuVendor.Nvidia or GpuVendor.Amd).ToList();

        if (integrated.Count == 0 || discrete.Count == 0) return [];

        return
        [
            new Finding(
                RuleId: Id,
                Category: Category,
                Severity: Severity.Information,
                Title: "Configuration graphique hybride",
                Evidence: string.Join(
                    Environment.NewLine,
                    gpus.Select(g => $"  {g.Name} [{g.Vendor}] pilote {g.DriverVersion ?? "inconnu"}")),
                Explanation:
                    "Cette machine possede deux processeurs graphiques : un integre, econome, et un " +
                    "discret, puissant. Windows choisit lequel utiliser application par application. " +
                    "Une application qui s'execute sur le processeur integre alors qu'elle demande de " +
                    "la puissance affichera des performances tres inferieures a ce que le materiel " +
                    "permet. C'est la cause la plus frequente de deceptions sur ce type de machine.",
                SuggestedFix: null,
                FixRisk: null),
        ];
    }
}
