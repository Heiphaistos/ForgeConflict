namespace ForgeConflict.Core.Safety;

/// <summary>
/// Compte les opérations qui installent ou modifient le système (pilotes, magasin de pilotes,
/// point de restauration). La fermeture de l'application le consulte pour ne
/// jamais couper une installation sans prévenir. Thread-safe.
/// </summary>
public static class InstallTracker
{
    private static readonly object Gate = new();
    private static readonly List<string> Running = [];

    /// <summary>Levé (sur le thread qui termine) quand la dernière opération se termine.</summary>
    public static event Action? Idle;

    /// <summary>À utiliser avec <c>using</c> : le compteur redescend même en cas d'exception.</summary>
    public static IDisposable Begin(string name)
    {
        lock (Gate) Running.Add(name);
        return new Scope(name);
    }

    public static bool IsBusy { get { lock (Gate) return Running.Count > 0; } }

    /// <summary>Noms des opérations en cours (sans doublon), ou null si aucune.</summary>
    public static string? CurrentName
    {
        get
        {
            lock (Gate)
                return Running.Count == 0 ? null : string.Join(" et ", Running.Distinct());
        }
    }

    private static void End(string name)
    {
        bool idle;
        lock (Gate)
        {
            Running.Remove(name);
            idle = Running.Count == 0;
        }
        if (idle) Idle?.Invoke();
    }

    private sealed class Scope(string name) : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) End(name);
        }
    }
}
