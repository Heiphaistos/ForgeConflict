using ForgeConflict.Core.Logging;

namespace ForgeConflict.Core.Safety;

public sealed record TransactionResult(bool Succeeded, string Message, Transaction Transaction);

/// <summary>
/// Execute une transaction en garantissant la reversibilite.
///
/// L'ordre est imperatif : le plan de restauration est ecrit AVANT la premiere
/// modification. Une coupure a n'importe quel instant laisse donc soit un etat
/// coherent, soit une transaction ouverte que la reprise au demarrage saura defaire.
/// </summary>
public sealed class TransactionRunner
{
    private readonly ITransactionStore _store;
    private readonly IChangeApplier _applier;
    private readonly ILog _log;

    public TransactionRunner(ITransactionStore store, IChangeApplier applier, ILog log)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(applier);
        ArgumentNullException.ThrowIfNull(log);

        _store = store;
        _applier = applier;
        _log = log;
    }

    public TransactionResult Run(Transaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        // 1. Le plan de restauration d'abord, sans exception.
        _store.Write(transaction);
        _log.Write(LogLevel.Info, $"transaction {transaction.Id:N} ouverte : {transaction.Title}");

        var applied = new List<ChangeRecord>();

        try
        {
            foreach (var change in transaction.Changes)
            {
                _applier.Apply(change);
                applied.Add(change);
            }
        }
        catch (Exception ex)
        {
            _log.Write(LogLevel.Error, $"transaction {transaction.Id:N} : echec, retour arriere", ex);

            RevertAll(applied);
            var restored = transaction.RolledBack(DateTimeOffset.Now);
            _store.Write(restored);

            return new TransactionResult(false, $"Echec : {ex.Message}. L'etat precedent a ete retabli.", restored);
        }

        var committed = transaction.Commit(DateTimeOffset.Now);
        _store.Write(committed);
        _log.Write(LogLevel.Info, $"transaction {transaction.Id:N} validee");

        return new TransactionResult(true, "Modification appliquee.", committed);
    }

    public TransactionResult Rollback(Transaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        var failures = RevertAll(transaction.Changes);
        var restored = transaction.RolledBack(DateTimeOffset.Now);
        _store.Write(restored);

        return failures == 0
            ? new TransactionResult(true, "Etat precedent retabli.", restored)
            : new TransactionResult(false,
                $"Etat retabli partiellement : {failures} modification(s) n'ont pas pu etre defaites.", restored);
    }

    /// <summary>
    /// Defait en ordre inverse et poursuit malgre les echecs : s'arreter a la premiere
    /// difficulte laisserait la machine dans un etat intermediaire, pire que l'etat de
    /// depart comme que l'etat cible.
    /// </summary>
    private int RevertAll(IReadOnlyList<ChangeRecord> changes)
    {
        var failures = 0;

        for (var i = changes.Count - 1; i >= 0; i--)
        {
            try
            {
                _applier.Revert(changes[i]);
            }
            catch (Exception ex)
            {
                failures++;
                _log.Write(LogLevel.Error, $"restauration impossible : {changes[i].Describe()}", ex);
            }
        }

        return failures;
    }
}
