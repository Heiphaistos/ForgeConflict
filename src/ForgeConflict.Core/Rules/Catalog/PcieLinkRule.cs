using ForgeConflict.Core.Inventory;
using ForgeConflict.Core.Safety;

namespace ForgeConflict.Core.Rules.Catalog;

/// <summary>
/// Signale un lien PCIe fonctionnant sous son maximum. La bande passante disponible
/// pour le GPU en est reduite.
/// </summary>
public sealed class PcieLinkRule : IRule
{
    public string Id => "FG-HW-001";

    public RuleCategory Category => RuleCategory.HardwareConfig;

    public IReadOnlyList<Finding> Evaluate(SystemSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!snapshot.PcieLinks.IsUsable || snapshot.PcieLinks.Value is not { } links)
        {
            return [];
        }

        var findings = new List<Finding>();

        foreach (var (adapter, link) in links)
        {
            // Un lien inconnu n'est pas un lien degrade : un GPU integre au processeur
            // n'expose aucune propriete PCIe parce qu'il n'est sur aucun lien.
            if (!link.IsKnown || !link.IsDegraded) continue;

            var portable = IsLikelyLaptop(snapshot);

            findings.Add(new Finding(
                RuleId: Id,
                Category: Category,
                // Sur portable, un cablage en x8 est un choix du constructeur et non
                // une anomalie : le signaler comme un probleme serait une fausse alerte.
                Severity: portable ? Severity.Information : Severity.Moyen,
                Title: $"Lien PCIe reduit sur {adapter}",
                Evidence: $"{adapter} : {link.Describe()}",
                Explanation: portable
                    ? "La carte fonctionne sous sa largeur maximale. Sur un ordinateur portable, " +
                      "c'est le plus souvent le cablage voulu par le constructeur et non un defaut : " +
                      "la carte est reliee par moins de lignes qu'elle ne pourrait en utiliser. " +
                      "Aucune action n'est necessaire si les performances sont conformes a vos attentes."
                    : "La carte fonctionne sous sa largeur ou sa vitesse maximale, ce qui reduit la " +
                      "bande passante entre le processeur et le GPU. Les causes habituelles sont un " +
                      "connecteur secondaire de la carte mere, une carte mal enfoncee, ou une gestion " +
                      "d'energie qui bride le lien au repos.",
                SuggestedFix: portable
                    ? null
                    : "Verifier que la carte occupe le connecteur principal, qu'elle est correctement " +
                      "enfoncee, et que le mode d'economie d'energie du lien PCI Express est desactive.",
                FixRisk: portable ? null : RiskLevel.Moyen));
        }

        return findings;
    }

    /// <summary>
    /// Un adaptateur dont le nom mentionne « Laptop » ou « Mobile », ou la presence
    /// simultanee d'un GPU integre et d'un GPU discret, trahit une configuration
    /// hybride de portable.
    /// </summary>
    private static bool IsLikelyLaptop(SystemSnapshot snapshot)
    {
        if (!snapshot.Gpus.IsUsable || snapshot.Gpus.Value is not { } gpus) return false;

        if (gpus.Any(g =>
                g.Name.Contains("Laptop", StringComparison.OrdinalIgnoreCase) ||
                g.Name.Contains("Mobile", StringComparison.OrdinalIgnoreCase) ||
                g.Name.Contains("Max-Q", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var hasIntegrated = gpus.Any(g => g.Vendor == GpuVendor.Intel);
        var hasDiscrete = gpus.Any(g => g.Vendor is GpuVendor.Nvidia or GpuVendor.Amd);

        return hasIntegrated && hasDiscrete;
    }
}
