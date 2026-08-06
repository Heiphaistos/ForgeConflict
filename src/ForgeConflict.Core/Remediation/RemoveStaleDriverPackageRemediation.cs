using ForgeConflict.Core.Inventory;
using ForgeConflict.Core.Inventory.Model;
using ForgeConflict.Core.Rules;
using ForgeConflict.Core.Safety;

namespace ForgeConflict.Core.Remediation;

/// <summary>
/// Retire du magasin les paquets de pilote obsoletes issus d'un meme fichier
/// d'installation, en conservant la version la plus recente.
///
/// Risque critique : l'operation touche au magasin de pilotes et n'est pas
/// reversible sans point de restauration. Le paquet actif n'est JAMAIS touche.
/// </summary>
public sealed class RemoveStaleDriverPackageRemediation : IRemediation
{
    public string RuleId => "FG-DRV-001";

    public string Title => "Retirer les paquets de pilote obsoletes";

    public RiskLevel Risk => RiskLevel.Critique;

    public bool AffectsDisplay => true;

    public bool RequiresReboot => false;

    public string Explanation =>
        "Les paquets de pilote plus anciens seront retires du magasin de Windows. " +
        "Le paquet actuellement utilise n'est pas touche : seules les versions " +
        "anciennes, laissees par des mises a jour precedentes, sont supprimees. " +
        "Un point de restauration systeme est cree avant l'operation, car un paquet " +
        "retire du magasin ne peut pas etre remis en place automatiquement.";

    public bool Handles(Finding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);
        return string.Equals(finding.RuleId, RuleId, StringComparison.Ordinal);
    }

    public IReadOnlyList<ChangeRecord> Preview(SystemSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!snapshot.DriverPackages.IsUsable || snapshot.DriverPackages.Value is not { } packages)
        {
            return [];
        }

        var changes = new List<ChangeRecord>();

        foreach (var group in packages
                     .Where(p => p.IsDisplayClass && !string.IsNullOrWhiteSpace(p.OriginalName))
                     .GroupBy(p => p.OriginalName!, StringComparer.OrdinalIgnoreCase)
                     .Where(g => g.Count() > 1))
        {
            // Le tri numerique est capital : en comparaison textuelle, « 9.x »
            // passerait apres « 30.x » et la remediation supprimerait le paquet actif.
            var stale = group
                .OrderByDescending(p => ParseVersion(p.DriverVersion))
                .Skip(1);

            foreach (var package in stale)
            {
                changes.Add(new ChangeRecord(
                    Kind: ChangeKind.ScheduledTask,
                    Target: package.PublishedName,
                    ValueName: package.OriginalName,
                    PreviousValue: package.DriverVersion,
                    NewValue: null,
                    PreviousValueExisted: true,
                    AffectsDisplay: true));
            }
        }

        return changes;
    }

    private static Version ParseVersion(string? raw) =>
        Version.TryParse(raw, out var version) ? version : new Version(0, 0);
}
