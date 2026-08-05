namespace ForgeConflict.Core.Collection;

public enum FragmentStatus
{
    /// <summary>La collecte a abouti, la valeur est exploitable.</summary>
    Ok,

    /// <summary>La collecte a leve une exception. La cause est dans Error.</summary>
    Failed,

    /// <summary>La collecte n'a pas rendu la main dans le delai imparti.</summary>
    TimedOut,
}
