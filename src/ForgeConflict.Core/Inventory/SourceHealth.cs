using ForgeConflict.Core.Collection;

namespace ForgeConflict.Core.Inventory;

public enum HealthLevel
{
    Inconnu,
    Sain,
    Attention,
    Critique,
}

/// <summary>
/// Traduit l'etat d'une source en niveau de sante lisible, avec l'explication
/// correspondante. Un delai depasse et un echec ne sont pas equivalents : le premier
/// laisse esperer une reponse, le second signale une source inaccessible.
/// </summary>
public sealed record SourceHealth(string Source, HealthLevel Level, string Explanation, TimeSpan Duration)
{
    public static SourceHealth FromFragment(IFragment fragment)
    {
        ArgumentNullException.ThrowIfNull(fragment);

        var (level, explanation) = fragment.Status switch
        {
            FragmentStatus.Ok => (HealthLevel.Sain, "Source interrogee avec succes."),
            FragmentStatus.TimedOut => (HealthLevel.Attention,
                "La source n'a pas repondu dans le delai imparti. Le diagnostic est incomplet."),
            _ => (HealthLevel.Critique,
                $"La source est inaccessible : {fragment.Error ?? "cause inconnue"}."),
        };

        return new SourceHealth(fragment.Source, level, explanation, fragment.Duration);
    }

    /// <summary>
    /// Niveau global : le pire des niveaux observes. Une liste vide rend
    /// <see cref="HealthLevel.Inconnu"/> et non <see cref="HealthLevel.Sain"/> :
    /// une absence de mesure n'est pas un bon resultat.
    /// </summary>
    public static HealthLevel Overall(IReadOnlyCollection<HealthLevel> levels)
    {
        ArgumentNullException.ThrowIfNull(levels);
        if (levels.Count == 0) return HealthLevel.Inconnu;

        if (levels.Contains(HealthLevel.Critique)) return HealthLevel.Critique;
        if (levels.Contains(HealthLevel.Attention)) return HealthLevel.Attention;
        if (levels.Contains(HealthLevel.Inconnu)) return HealthLevel.Inconnu;

        return HealthLevel.Sain;
    }
}
