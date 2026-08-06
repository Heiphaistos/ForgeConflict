using ForgeConflict.Core.Logging;

namespace ForgeConflict.Core.Safety;

/// <summary>
/// Relit le journal a chaque demarrage. Une transaction encore ouverte signale une
/// interruption : plantage, ecran noir ou coupure de courant pendant l'application.
///
/// Celles qui touchaient l'affichage sont defaites AUTOMATIQUEMENT : si l'ecran est
/// noir, l'utilisateur n'a aucun moyen de repondre a une question. Les autres lui
/// sont presentees, car rien n'urge et le choix lui revient.
/// </summary>
public sealed class RecoveryService
{
    private readonly ITransactionStore _store;
    private readonly IChangeApplier _applier;
    private readonly ILog _log;

    public RecoveryService(ITransactionStore store, IChangeApplier applier, ILog log)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(applier);
        ArgumentNullException.ThrowIfNull(log);

        _store = store;
        _applier = applier;
        _log = log;
    }

    public IReadOnlyList<Transaction> Scan()
    {
        var pending = _store.ReadPending();

        if (pending.Count > 0)
        {
            _log.Write(LogLevel.Warn,
                $"{pending.Count} transaction(s) interrompue(s) trouvee(s) au demarrage");
        }

        return pending;
    }

    /// <summary>Defait sans demander les transactions qui touchaient l'affichage.</summary>
    public int RecoverAutomatically() => Recover(t => t.AffectsDisplay);

    /// <summary>Defait toutes les transactions en attente, sur decision de l'utilisateur.</summary>
    public int RecoverAll() => Recover(_ => true);

    private int Recover(Func<Transaction, bool> predicate)
    {
        var runner = new TransactionRunner(_store, _applier, _log);
        var count = 0;

        foreach (var transaction in Scan().Where(predicate))
        {
            _log.Write(LogLevel.Warn,
                $"restauration de la transaction interrompue {transaction.Id:N} : {transaction.Title}");

            runner.Rollback(transaction);
            count++;
        }

        return count;
    }
}
