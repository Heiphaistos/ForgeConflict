using ForgeConflict.Core.Inventory;
using ForgeConflict.Core.Safety;

namespace ForgeConflict.Core.Rules.Catalog;

/// <summary>
/// Signale un paquet de pilote d'affichage sans signature. Windows exige normalement
/// une signature pour charger un pilote noyau : un paquet non signe indique soit un
/// mode test actif, soit un pilote d'origine douteuse.
/// </summary>
public sealed class UnsignedDriverRule : IRule
{
    public string Id => "FG-DRV-003";

    public RuleCategory Category => RuleCategory.DriverConflict;

    public IReadOnlyList<Finding> Evaluate(SystemSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!snapshot.DriverPackages.IsUsable || snapshot.DriverPackages.Value is not { } packages)
        {
            return [];
        }

        var unsigned = packages.Where(p => p.IsDisplayClass && p.IsUnsigned).ToList();
        if (unsigned.Count == 0) return [];

        return
        [
            new Finding(
                RuleId: Id,
                Category: Category,
                Severity: Severity.Eleve,
                Title: $"{unsigned.Count} paquet(s) de pilote d'affichage sans signature",
                Evidence: string.Join(
                    Environment.NewLine,
                    unsigned.Select(p =>
                        $"  {p.PublishedName} — {p.ProviderName ?? "fournisseur inconnu"} " +
                        $"version {p.DriverVersion ?? "inconnue"}")),
                Explanation:
                    "Ces paquets ne portent aucune signature numerique. Windows refuse normalement de " +
                    "charger un pilote non signe : leur presence indique que le mode test de signature " +
                    "est actif, ou que le paquet provient d'une source non verifiee. Un pilote " +
                    "graphique s'execute au niveau du noyau, ou une defaillance fige la machine entiere.",
                SuggestedFix:
                    "Verifier l'origine de ces paquets et les remplacer par les versions signees du constructeur.",
                FixRisk: RiskLevel.Eleve),
        ];
    }
}
