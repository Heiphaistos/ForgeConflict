namespace ForgeConflict.Core.Collection;

/// <summary>Vue non generique d'un fragment, pour presenter des sources heterogenes ensemble.</summary>
public interface IFragment
{
    string Source { get; }
    FragmentStatus Status { get; }
    string? Error { get; }
    TimeSpan Duration { get; }
    bool IsUsable { get; }
}

/// <summary>
/// Resultat d'un collecteur. Un fragment inexploitable n'interrompt jamais le scan :
/// il est presente tel quel a l'utilisateur pour qu'aucun diagnostic partiel ne passe
/// pour un diagnostic complet.
/// </summary>
public sealed record Fragment<T>(
    string Source,
    FragmentStatus Status,
    T? Value,
    string? Error,
    TimeSpan Duration) : IFragment
{
    public bool IsUsable => Status == FragmentStatus.Ok;

    public static Fragment<T> Ok(string source, T value, TimeSpan duration) =>
        new(source, FragmentStatus.Ok, value, null, duration);

    public static Fragment<T> Failed(string source, string error, TimeSpan duration) =>
        new(source, FragmentStatus.Failed, default, error, duration);

    public static Fragment<T> TimedOut(string source, TimeSpan duration) =>
        new(source, FragmentStatus.TimedOut, default, null, duration);
}
