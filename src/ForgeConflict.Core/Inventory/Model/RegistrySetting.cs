using System.Globalization;

namespace ForgeConflict.Core.Inventory.Model;

/// <summary>
/// Reglage registre lu sur la machine. Une valeur absente n'est PAS zero : elle signifie
/// que Windows applique sa valeur par defaut, souvent differente. Confondre les deux
/// conduirait a diagnostiquer un probleme inexistant, ou pire, a en masquer un reel.
/// </summary>
public sealed record RegistrySetting(
    string Name,
    bool IsPresent,
    object? RawValue,
    object? EffectiveDefault)
{
    /// <summary>Valeur reellement appliquee par Windows : celle lue, ou le defaut si absente.</summary>
    public object? EffectiveValue => IsPresent ? RawValue : EffectiveDefault;

    public bool IsDefault => Equals(EffectiveValue, EffectiveDefault);

    public static RegistrySetting Absent(string name, object? effectiveDefault) =>
        new(name, false, null, effectiveDefault);

    public static RegistrySetting Present(string name, object? value, object? effectiveDefault) =>
        new(name, true, value, effectiveDefault);

    public string Describe()
    {
        var value = Convert.ToString(EffectiveValue, CultureInfo.InvariantCulture) ?? "-";
        return IsPresent
            ? $"{Name} : valeur definie a {value}"
            : $"{Name} : valeur non definie, Windows applique {value}";
    }
}
