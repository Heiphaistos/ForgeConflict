namespace ForgeConflict.Core.Safety;

/// <summary>
/// Depot des transactions. L'ecriture doit etre durable AVANT que l'action
/// correspondante soit appliquee : c'est cette garantie qui rend une interruption
/// recuperable.
/// </summary>
public interface ITransactionStore
{
    void Write(Transaction transaction);

    IReadOnlyList<Transaction> ReadAll();

    IReadOnlyList<Transaction> ReadPending();
}
