using ForgeConflict.Core.Inventory;

namespace ForgeConflict.Core.Rules;

/// <summary>
/// Une regle CONSTATE, elle ne corrige jamais. Cette separation est structurelle :
/// cet assemblage ne contient aucun code d'ecriture, et une regle ne peut donc pas
/// modifier la machine, meme par accident.
///
/// Une regle rend zero, un ou plusieurs constats. Rendre une liste vide est le cas
/// normal sur une machine saine, jamais un echec.
/// </summary>
public interface IRule
{
    /// <summary>Identifiant stable, repris dans l'interface et le journal.</summary>
    string Id { get; }

    RuleCategory Category { get; }

    IReadOnlyList<Finding> Evaluate(SystemSnapshot snapshot);
}
