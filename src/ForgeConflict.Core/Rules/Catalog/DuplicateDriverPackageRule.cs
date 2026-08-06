using System.Globalization;
using ForgeConflict.Core.Inventory;
using ForgeConflict.Core.Inventory.Model;
using ForgeConflict.Core.Safety;

namespace ForgeConflict.Core.Rules.Catalog;

/// <summary>
/// Detecte plusieurs paquets de pilote d'affichage du meme fournisseur dans le
/// magasin. Le plus souvent un residu de mise a jour : l'ancienne version reste
/// publiee alors qu'une nouvelle est active.
/// </summary>
public sealed class DuplicateDriverPackageRule : IRule
{
    public string Id => "FG-DRV-001";

    public RuleCategory Category => RuleCategory.DriverConflict;

    public IReadOnlyList<Finding> Evaluate(SystemSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!snapshot.DriverPackages.IsUsable || snapshot.DriverPackages.Value is not { } packages)
        {
            return [];
        }

        var findings = new List<Finding>();

        // Le regroupement se fait sur le fichier INF d'origine et non sur le
        // fournisseur : deux paquets Intel pour des materiels differents sont
        // legitimes, deux paquets issus du meme INF ne le sont pas.
        foreach (var group in packages
                     .Where(p => p.IsDisplayClass && !string.IsNullOrWhiteSpace(p.OriginalName))
                     .GroupBy(p => p.OriginalName!, StringComparer.OrdinalIgnoreCase)
                     .Where(g => g.Count() > 1))
        {
            var ordered = group
                .OrderByDescending(p => ParseVersion(p.DriverVersion))
                .ToList();

            var active = ordered[0];
            var stale = ordered.Skip(1).ToList();

            var evidence = string.Join(
                Environment.NewLine,
                ordered.Select(p => string.Create(CultureInfo.CurrentCulture,
                    $"  {p.PublishedName} — version {p.DriverVersion ?? "inconnue"} ({p.ProviderName ?? "fournisseur inconnu"})")));

            findings.Add(new Finding(
                RuleId: Id,
                Category: Category,
                Severity: Severity.Moyen,
                Title: $"{group.Count()} paquets de pilote issus de {group.Key}",
                Evidence: evidence,
                Explanation:
                    $"Le magasin de pilotes contient {group.Count()} paquets provenant du meme fichier " +
                    $"d'installation. La version {active.DriverVersion ?? "la plus recente"} est la plus recente ; " +
                    "les autres sont des residus d'une mise a jour precedente. Windows peut, lors d'une " +
                    "reinstallation ou d'une restauration de pilote, choisir un de ces paquets plus anciens.",
                SuggestedFix:
                    $"Retirer du magasin les {stale.Count} paquet(s) obsolete(s) : " +
                    string.Join(", ", stale.Select(p => p.PublishedName)) + ".",
                FixRisk: RiskLevel.Eleve));
        }

        return findings;
    }

    /// <summary>
    /// Compare les versions numeriquement. Une comparaison textuelle placerait
    /// « 9 » apres « 30 », donc designerait le mauvais paquet comme le plus recent.
    /// </summary>
    private static Version ParseVersion(string? raw) =>
        Version.TryParse(raw, out var version) ? version : new Version(0, 0);
}
