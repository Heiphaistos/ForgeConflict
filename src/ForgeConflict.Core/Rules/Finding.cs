using ForgeConflict.Core.Safety;

namespace ForgeConflict.Core.Rules;

/// <summary>
/// Gravite d'un CONSTAT : a quel point le probleme detecte nuit a la machine.
/// A ne pas confondre avec <see cref="RiskLevel"/>, qui qualifie le risque d'une
/// ACTION. Un probleme critique peut avoir une correction sans danger, et
/// l'inverse est tout aussi vrai.
/// </summary>
public enum Severity
{
    Information,
    Moyen,
    Eleve,
    Critique,
}

public enum RuleCategory
{
    DriverConflict,
    ServiceConflict,
    ApplicationConflict,
    HardwareConfig,
    CrashHistory,
    DriverVersion,
}

/// <summary>
/// Constat produit par une regle. Il repond toujours aux quatre questions dans cet
/// ordre : ce qui a ete trouve, pourquoi c'est un probleme, ce qu'une correction
/// ferait, et ce qu'elle risque.
///
/// Un constat ne corrige rien. Il decrit.
/// </summary>
public sealed record Finding(
    string RuleId,
    RuleCategory Category,
    Severity Severity,
    string Title,
    string Evidence,
    string Explanation,
    string? SuggestedFix,
    RiskLevel? FixRisk)
{
    /// <summary>Vrai si une correction existe et pourra etre proposee au lot 5.</summary>
    public bool HasFix => !string.IsNullOrWhiteSpace(SuggestedFix);
}
