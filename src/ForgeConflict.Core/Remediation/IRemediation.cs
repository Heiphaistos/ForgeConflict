using ForgeConflict.Core.Inventory;
using ForgeConflict.Core.Rules;
using ForgeConflict.Core.Safety;

namespace ForgeConflict.Core.Remediation;

/// <summary>
/// Correction proposee pour un constat. Elle ne s'applique JAMAIS d'elle-meme :
/// l'appelant doit d'abord montrer <see cref="Preview"/> a l'utilisateur, obtenir
/// son accord explicite, puis executer la transaction.
/// </summary>
public interface IRemediation
{
    /// <summary>Identifiant de la regle dont cette correction traite le constat.</summary>
    string RuleId { get; }

    string Title { get; }

    RiskLevel Risk { get; }

    /// <summary>Vrai si l'affichage peut etre perturbe : le watchdog devient obligatoire.</summary>
    bool AffectsDisplay { get; }

    bool RequiresReboot { get; }

    /// <summary>Ce que la correction fait, en francais, pour un non-specialiste.</summary>
    string Explanation { get; }

    /// <summary>Vrai si cette correction traite le constat fourni.</summary>
    bool Handles(Finding finding);

    /// <summary>
    /// Liste exacte des modifications, valeur avant et valeur apres. Rien n'est cache :
    /// c'est ce que l'utilisateur voit avant de decider.
    /// </summary>
    IReadOnlyList<ChangeRecord> Preview(SystemSnapshot snapshot);
}

/// <summary>
/// Fabrique la transaction correspondante. Separee de l'interface pour qu'une
/// remediation reste une simple description tant que personne n'a confirme.
/// </summary>
public static class RemediationExtensions
{
    public static Transaction BuildTransaction(this IRemediation remediation, SystemSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(remediation);
        ArgumentNullException.ThrowIfNull(snapshot);

        var changes = remediation.Preview(snapshot);

        if (changes.Count == 0)
        {
            throw new InvalidOperationException(
                $"La correction « {remediation.Title} » n'a rien a modifier sur cette machine.");
        }

        return Transaction.Open(remediation.RuleId, remediation.Title, remediation.Risk, changes);
    }
}
