namespace ForgeConflict.Core.Inventory.Model;

/// <summary>
/// Evenement du journal Windows attribue a un pilote graphique. Le fournisseur est
/// conserve : un identifiant d'evenement n'a de sens qu'associe a son fournisseur.
/// </summary>
public sealed record GpuEvent(
    DateTimeOffset TimeCreated,
    int EventId,
    string ProviderName,
    string? Level,
    string? Message)
{
    /// <summary>Reinitialisation du pilote apres depassement du delai de reponse (TDR).</summary>
    public bool IsTimeoutRecovery => EventId is 4101 or 4102;
}
