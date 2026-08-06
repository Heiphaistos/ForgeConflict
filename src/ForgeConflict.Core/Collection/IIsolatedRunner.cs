namespace ForgeConflict.Core.Collection;

/// <summary>
/// Execute un travail de collecte en isolation : ni exception ni blocage ne remontent
/// a l'appelant. C'est la garantie qu'un collecteur defaillant n'interrompt pas le scan.
/// </summary>
public interface IIsolatedRunner
{
    Task<Fragment<T>> RunAsync<T>(
        string source,
        Func<CancellationToken, T> work,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}
