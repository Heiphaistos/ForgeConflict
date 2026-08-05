using ForgeConflict.Core.Logging;

namespace ForgeConflict.Core.Diagnostics;

/// <summary>
/// Recueille les exceptions non gerees. L'application manipule des pilotes graphiques :
/// se fermer brutalement en pleine operation serait la pire issue possible, donc rien
/// ne doit provoquer un arret non maitrise.
/// </summary>
public sealed class ExceptionGuard
{
    private readonly ILog _log;
    private int _handledCount;

    public ExceptionGuard(ILog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        _log = log;
    }

    public int HandledCount => Volatile.Read(ref _handledCount);

    /// <returns>Toujours vrai : l'exception est consideree traitee, l'interface survit.</returns>
    public bool HandleUi(Exception exception)
    {
        Interlocked.Increment(ref _handledCount);
        _log.Write(LogLevel.Error, "exception non geree sur le thread de l'interface", exception);
        return true;
    }

    public void HandleDomain(object? exceptionObject, bool isTerminating)
    {
        Interlocked.Increment(ref _handledCount);
        var exception = exceptionObject as Exception;
        var suffix = isTerminating ? " (arret en cours)" : string.Empty;
        _log.Write(LogLevel.Error, $"exception non geree du domaine{suffix} : {exceptionObject}", exception);
    }

    /// <returns>Toujours vrai : l'exception est marquee observee pour ne pas tuer le processus.</returns>
    public bool HandleUnobservedTask(Exception exception)
    {
        Interlocked.Increment(ref _handledCount);
        _log.Write(LogLevel.Error, "exception de tache non observee", exception);
        return true;
    }
}
