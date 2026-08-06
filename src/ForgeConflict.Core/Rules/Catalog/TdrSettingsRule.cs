using System.Globalization;
using ForgeConflict.Core.Inventory;
using ForgeConflict.Core.Safety;

namespace ForgeConflict.Core.Rules.Catalog;

/// <summary>
/// Examine les reglages de recuperation du pilote graphique (TDR). Ces valeurs
/// determinent au bout de combien de temps Windows considere le GPU bloque et
/// reinitialise le pilote.
/// </summary>
public sealed class TdrSettingsRule : IRule
{
    public string Id => "FG-TDR-001";

    public RuleCategory Category => RuleCategory.DriverConflict;

    public IReadOnlyList<Finding> Evaluate(SystemSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!snapshot.GraphicsSettings.IsUsable || snapshot.GraphicsSettings.Value is not { } settings)
        {
            return [];
        }

        var findings = new List<Finding>();

        // TdrLevel = 0 desactive completement la recuperation : un GPU bloque fige
        // alors la machine au lieu d'etre reinitialise. C'est le reglage le plus
        // dangereux de cette cle.
        if (settings.TdrLevel.IsPresent && IsZero(settings.TdrLevel.EffectiveValue))
        {
            findings.Add(new Finding(
                RuleId: Id,
                Category: Category,
                Severity: Severity.Critique,
                Title: "La recuperation automatique du pilote graphique est desactivee",
                Evidence: settings.TdrLevel.Describe(),
                Explanation:
                    "TdrLevel vaut 0 : Windows ne reinitialise plus le pilote graphique quand il " +
                    "cesse de repondre. Au lieu d'un bref scintillement suivi d'un retour a la normale, " +
                    "un blocage du GPU fige desormais toute la machine et impose un redemarrage force. " +
                    "Ce reglage est parfois pose par des outils de diagnostic et oublie ensuite.",
                SuggestedFix: "Retablir TdrLevel a 3, la valeur par defaut de Windows, qui reactive la recuperation.",
                FixRisk: RiskLevel.Eleve));
        }

        // Un delai allonge est une rustine : il masque des blocages au lieu de les
        // resoudre, et rallonge d'autant le gel percu par l'utilisateur.
        if (settings.TdrDelay.IsPresent && ToInt(settings.TdrDelay.EffectiveValue) > 10)
        {
            findings.Add(new Finding(
                RuleId: Id,
                Category: Category,
                Severity: Severity.Moyen,
                Title: "Le delai avant recuperation du pilote est tres allonge",
                Evidence: settings.TdrDelay.Describe(),
                Explanation:
                    $"TdrDelay vaut {ToInt(settings.TdrDelay.EffectiveValue)} secondes, contre 2 par defaut. " +
                    "Ce reglage est souvent applique pour faire disparaitre des messages de " +
                    "reinitialisation du pilote, mais il ne corrige pas leur cause : il rallonge " +
                    "simplement le gel de l'ecran avant que Windows n'intervienne.",
                SuggestedFix: "Ramener TdrDelay a 2 secondes et rechercher la cause reelle des blocages.",
                FixRisk: RiskLevel.Eleve));
        }

        return findings;
    }

    private static bool IsZero(object? value) => ToInt(value) == 0;

    private static int ToInt(object? value)
    {
        try
        {
            return value is null ? -1 : Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            // Une valeur d'un type inattendu ne doit pas faire tomber la regle :
            // -1 la neutralise sans declencher de faux constat.
            return -1;
        }
    }
}
