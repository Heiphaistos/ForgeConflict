namespace ForgeConflict.Core.Collection;

/// <summary>
/// Forme serialisable d'un <see cref="Fragment{T}"/>. La charge utile reste du JSON brut :
/// l'enveloppe n'a pas a connaitre les types de chaque collecteur.
/// </summary>
public sealed record FragmentRecord(
    string Source,
    FragmentStatus Status,
    string? Error,
    double DurationMs,
    string? PayloadJson);
