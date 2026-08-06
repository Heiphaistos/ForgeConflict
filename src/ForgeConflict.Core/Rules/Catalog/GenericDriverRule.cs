using ForgeConflict.Core.Inventory;
using ForgeConflict.Core.Safety;

namespace ForgeConflict.Core.Rules.Catalog;

/// <summary>
/// Detecte un adaptateur fonctionnant sur le pilote d'affichage generique de Windows.
/// C'est l'etat dans lequel se trouve une machine dont le pilote constructeur a ete
/// desinstalle, a echoue, ou n'a jamais ete installe.
/// </summary>
public sealed class GenericDriverRule : IRule
{
    public string Id => "FG-DRV-002";

    public RuleCategory Category => RuleCategory.DriverConflict;

    public IReadOnlyList<Finding> Evaluate(SystemSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!snapshot.Gpus.IsUsable || snapshot.Gpus.Value is not { } gpus) return [];

        var findings = new List<Finding>();

        foreach (var gpu in gpus.Where(g => g.IsGenericMicrosoftDriver))
        {
            // Un adaptateur virtualise n'a pas de pilote constructeur a installer :
            // le signaler comme un probleme serait une fausse alerte dans une VM.
            var virtualised = gpu.Name.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase) ||
                              gpu.Name.Contains("VMware", StringComparison.OrdinalIgnoreCase) ||
                              gpu.Name.Contains("VirtualBox", StringComparison.OrdinalIgnoreCase) ||
                              gpu.Name.Contains("Parsec", StringComparison.OrdinalIgnoreCase);

            if (virtualised) continue;

            findings.Add(new Finding(
                RuleId: Id,
                Category: Category,
                Severity: Severity.Eleve,
                Title: $"{gpu.Name} fonctionne sur le pilote generique de Windows",
                Evidence: $"{gpu.Name} — pilote {gpu.DriverVersion ?? "inconnu"} ({gpu.DeviceId})",
                Explanation:
                    "Aucun pilote constructeur n'est actif pour cet adaptateur : Windows utilise son " +
                    "pilote d'affichage de base. L'acceleration materielle, les resolutions elevees et " +
                    "les frequences de rafraichissement superieures sont indisponibles. Cet etat fait " +
                    "suite a une desinstallation, a une installation qui a echoue, ou a un pilote " +
                    "que Windows a desactive apres des plantages repetes.",
                SuggestedFix: "Installer le pilote fourni par le constructeur de la carte graphique.",
                FixRisk: RiskLevel.Eleve));
        }

        return findings;
    }
}
