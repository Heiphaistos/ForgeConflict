namespace ForgeConflict.Core.Inventory;

/// <summary>
/// Source d'inventaire. Un collecteur LIT l'etat du systeme et ne le modifie jamais :
/// cet assemblage ne contient aucun code d'ecriture, par construction.
/// </summary>
public interface ICollector<out T>
{
    /// <summary>Identifiant court, repris tel quel dans le journal et dans l'interface.</summary>
    string Source { get; }

    /// <summary>Delai au-dela duquel la collecte est abandonnee. WMI peut bloquer sans fin.</summary>
    TimeSpan Timeout { get; }

    T Collect(CancellationToken cancellationToken);
}
