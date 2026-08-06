using ForgeConflict.Core.Inventory;
using ForgeConflict.Core.Inventory.Model;
using ForgeConflict.Core.Rules;
using ForgeConflict.Core.Safety;

namespace ForgeConflict.Core.Remediation;

/// <summary>
/// Retablit les reglages de recuperation du pilote graphique a leur etat par defaut.
///
/// La correction SUPPRIME les valeurs au lieu d'y ecrire les defauts documentes :
/// une valeur absente laisse Windows appliquer son propre defaut, y compris si
/// celui-ci change avec une mise a jour. Ecrire « 3 » figerait ce choix pour
/// toujours, ce qui n'est pas la meme chose que revenir a l'etat d'origine.
/// </summary>
public sealed class RestoreTdrDefaultsRemediation : IRemediation
{
    private const string KeyPath = @"HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers";

    public string RuleId => "FG-TDR-001";

    public string Title => "Retablir les reglages de recuperation du pilote";

    public RiskLevel Risk => RiskLevel.Eleve;

    public bool AffectsDisplay => true;

    public bool RequiresReboot => true;

    public string Explanation =>
        "Les valeurs TdrLevel et TdrDelay seront retirees du registre. Windows reprendra " +
        "alors ses reglages d'origine : reinitialisation du pilote activee, et delai de " +
        "deux secondes avant d'agir. Le changement ne prend effet qu'apres un redemarrage. " +
        "L'operation est reversible : les valeurs actuelles sont enregistrees avant toute " +
        "modification.";

    public bool Handles(Finding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);
        return string.Equals(finding.RuleId, RuleId, StringComparison.Ordinal);
    }

    public IReadOnlyList<ChangeRecord> Preview(SystemSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!snapshot.GraphicsSettings.IsUsable || snapshot.GraphicsSettings.Value is not { } settings)
        {
            return [];
        }

        var changes = new List<ChangeRecord>();

        // Seules les valeurs REELLEMENT presentes sont touchees : supprimer une valeur
        // deja absente ne changerait rien et encombrerait l'apercu d'une ligne
        // trompeuse, laissant croire a une modification qui n'aura pas lieu.
        foreach (var setting in new[] { settings.TdrLevel, settings.TdrDelay, settings.TdrDdiDelay })
        {
            if (!setting.IsPresent) continue;

            changes.Add(new ChangeRecord(
                Kind: ChangeKind.RegistryValue,
                Target: KeyPath,
                ValueName: setting.Name,
                PreviousValue: setting.RawValue,
                NewValue: null, // null signifie « supprimer »
                PreviousValueExisted: true,
                AffectsDisplay: true));
        }

        return changes;
    }
}
