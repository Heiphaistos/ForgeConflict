using System.Windows.Media;

namespace ForgeConflict.App.Theme;

/// <summary>Base claire ou sombre demandee a WPF-UI pour les controles natifs.</summary>
public enum ThemeBase
{
    Dark,
    Light,
}

/// <summary>
/// Un theme complet. Les couleurs d'etat sont redefinies par theme et non partagees :
/// un vert lisible sur graphite devient illisible sur papier, et l'etat de sante est
/// precisement l'information qui ne doit jamais devenir difficile a lire.
/// </summary>
public sealed record ThemeDefinition(
    string Id,
    string Name,
    string Description,
    ThemeBase Base,
    Color Accent,
    Color Surface,
    Color SurfaceRaised,
    Color Border,
    Color TextPrimary,
    Color TextSecondary,
    Color Healthy,
    Color Warning,
    Color Critical,
    Color Unknown)
{
    public static Color Hex(string value) => (Color)ColorConverter.ConvertFromString(value);
}
