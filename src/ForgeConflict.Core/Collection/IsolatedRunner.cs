using System.Diagnostics;
using ForgeConflict.Core.Logging;

namespace ForgeConflict.Core.Collection;

public sealed class IsolatedRunner : IIsolatedRunner
{
    private readonly ILog _log;

    public IsolatedRunner(ILog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        _log = log;
    }

    public async Task<Fragment<T>> RunAsync<T>(
        string source,
        Func<CancellationToken, T> work,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(work);

        var stopwatch = Stopwatch.StartNew();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // CancellationToken.None : la tache doit pouvoir demarrer meme si l'annulation
        // est deja demandee, sinon on ne distingue plus l'echec du delai depasse.
        var task = Task.Run(() => work(cts.Token), CancellationToken.None);

        var finished = await Task.WhenAny(task, Task.Delay(timeout, CancellationToken.None)).ConfigureAwait(false);

        if (!ReferenceEquals(finished, task))
        {
            cts.Cancel();

            // Un appel natif bloque n'observe pas le jeton. On cesse de l'attendre,
            // mais on observe son exception eventuelle pour ne pas declencher
            // UnobservedTaskException, et on libere les ressources ensuite.
            _ = task.ContinueWith(
                t => { _ = t.Exception; cts.Dispose(); },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            _log.Write(LogLevel.Warn, $"collecteur {source} : delai depasse apres {timeout.TotalSeconds:0.#} s");
            return Fragment<T>.TimedOut(source, stopwatch.Elapsed);
        }

        try
        {
            var value = await task.ConfigureAwait(false);
            return Fragment<T>.Ok(source, value, stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            _log.Write(LogLevel.Error, $"collecteur {source} : echec de la collecte", ex);
            return Fragment<T>.Failed(source, ex.Message, stopwatch.Elapsed);
        }
        finally
        {
            cts.Dispose();
        }
    }
}
