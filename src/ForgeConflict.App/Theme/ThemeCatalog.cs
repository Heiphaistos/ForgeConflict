namespace ForgeConflict.App.Theme;

/// <summary>
/// Catalogue des themes. Chacun est une direction distincte, pas une variation de
/// teinte : fond, accent et couleurs d'etat sont accordes ensemble pour que le vert,
/// l'ambre et le rouge restent nettement distinguables sur leur propre fond.
/// </summary>
public static class ThemeCatalog
{
    private static readonly Func<string, System.Windows.Media.Color> C = ThemeDefinition.Hex;

    public static IReadOnlyList<ThemeDefinition> All { get; } =
    [
        new ThemeDefinition(
            Id: "graphite",
            Name: "Graphite",
            Description: "Sombre neutre, accent bleu acier. Le plus discret.",
            Base: ThemeBase.Dark,
            Accent: C("#4C8DDA"), Surface: C("#16181C"), SurfaceRaised: C("#1E2126"),
            Border: C("#2C3037"), TextPrimary: C("#E8EAED"), TextSecondary: C("#9AA0A8"),
            // L'ambre est nettement eclairci par rapport au vert : une simple difference
            // de teinte se perdrait pour un daltonien deutan, l'ecart de luminance non.
            Healthy: C("#3EA872"), Warning: C("#F0C24A"), Critical: C("#E05A47"), Unknown: C("#6B7079")),

        new ThemeDefinition(
            Id: "nocturne",
            Name: "Nocturne",
            Description: "Bleu nuit profond, accents cyan. Travail prolonge.",
            Base: ThemeBase.Dark,
            Accent: C("#3FBFD8"), Surface: C("#0E1621"), SurfaceRaised: C("#16202D"),
            Border: C("#22303F"), TextPrimary: C("#DCE7F0"), TextSecondary: C("#8AA0B4"),
            Healthy: C("#35A876"), Warning: C("#F2C654"), Critical: C("#E2604D"), Unknown: C("#61738A")),

        new ThemeDefinition(
            Id: "ardoise",
            Name: "Ardoise",
            Description: "Gris-bleu froid, accent indigo. Sobre et technique.",
            Base: ThemeBase.Dark,
            Accent: C("#7B8CDE"), Surface: C("#191B21"), SurfaceRaised: C("#22252D"),
            Border: C("#31353F"), TextPrimary: C("#E4E6EC"), TextSecondary: C("#969BA8"),
            Healthy: C("#3D9E72"), Warning: C("#F0C452"), Critical: C("#DC5F53"), Unknown: C("#6C7280")),

        new ThemeDefinition(
            Id: "cuivre",
            Name: "Cuivre",
            Description: "Sombre chaud, accents cuivre. Atelier et instrumentation.",
            Base: ThemeBase.Dark,
            Accent: C("#D08A50"), Surface: C("#1A1613"), SurfaceRaised: C("#241E1A"),
            Border: C("#352C25"), TextPrimary: C("#EFE6DC"), TextSecondary: C("#A89887"),
            Healthy: C("#41A166"), Warning: C("#F3C95E"), Critical: C("#DC5D45"), Unknown: C("#7A6E62")),

        new ThemeDefinition(
            Id: "foret",
            Name: "Foret",
            Description: "Vert profond, accents tilleul. Repos visuel.",
            Base: ThemeBase.Dark,
            Accent: C("#6FBF73"), Surface: C("#111815"), SurfaceRaised: C("#18211D"),
            Border: C("#243029"), TextPrimary: C("#E2EBE5"), TextSecondary: C("#8FA396"),
            // Le vert d'etat est deliberement plus clair que l'accent pour rester distinct.
            Healthy: C("#8FE0A0"), Warning: C("#E0AE3C"), Critical: C("#E2604D"), Unknown: C("#67786D")),

        new ThemeDefinition(
            Id: "amethyste",
            Name: "Amethyste",
            Description: "Violet sombre, accents lavande. Contraste doux.",
            Base: ThemeBase.Dark,
            Accent: C("#A98CE0"), Surface: C("#17141F"), SurfaceRaised: C("#201C2B"),
            Border: C("#2E283C"), TextPrimary: C("#E9E4F2"), TextSecondary: C("#9C94AE"),
            Healthy: C("#42A277"), Warning: C("#F1C557"), Critical: C("#E0605A"), Unknown: C("#6F6880")),

        new ThemeDefinition(
            Id: "terminal",
            Name: "Terminal",
            Description: "Noir intense, phosphore vert. Lecture de donnees brutes.",
            Base: ThemeBase.Dark,
            Accent: C("#4FD97A"), Surface: C("#0A0C0A"), SurfaceRaised: C("#111511"),
            Border: C("#1E271E"), TextPrimary: C("#D6F5D9"), TextSecondary: C("#7FA383"),
            Healthy: C("#34B85C"), Warning: C("#F5E06B"), Critical: C("#FF6B52"), Unknown: C("#5E735F")),

        new ThemeDefinition(
            Id: "ambre",
            Name: "Ambre",
            Description: "Sombre, cadran ambre. Inspire des instruments de mesure.",
            Base: ThemeBase.Dark,
            Accent: C("#E0A63C"), Surface: C("#141210"), SurfaceRaised: C("#1D1A16"),
            Border: C("#2B261F"), TextPrimary: C("#F0E7D8"), TextSecondary: C("#A99A83"),
            Healthy: C("#45A874"), Warning: C("#F5CE5C"), Critical: C("#E4614A"), Unknown: C("#75695A")),

        new ThemeDefinition(
            Id: "papier",
            Name: "Papier",
            Description: "Clair chaud, encre profonde. Lecture longue en plein jour.",
            Base: ThemeBase.Light,
            Accent: C("#2D6BA8"), Surface: C("#F6F3EC"), SurfaceRaised: C("#FFFDF8"),
            Border: C("#DCD5C7"), TextPrimary: C("#22252A"), TextSecondary: C("#6A6459"),
            // Sur fond clair, les couleurs d'etat sont assombries : les teintes vives
            // du mode sombre y perdraient tout contraste.
            Healthy: C("#146238"), Warning: C("#B98410"), Critical: C("#A83322"), Unknown: C("#7A7568")),

        new ThemeDefinition(
            Id: "albatre",
            Name: "Albatre",
            Description: "Clair froid et net. Captures d'ecran et documentation.",
            Base: ThemeBase.Light,
            Accent: C("#2F6FE0"), Surface: C("#F2F4F7"), SurfaceRaised: C("#FFFFFF"),
            Border: C("#D5DAE2"), TextPrimary: C("#1B1E23"), TextSecondary: C("#5D6672"),
            Healthy: C("#116039"), Warning: C("#B4800F"), Critical: C("#B03227"), Unknown: C("#727A85")),
    ];

    public const string DefaultId = "graphite";

    public static ThemeDefinition Get(string? id) =>
        All.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase))
        ?? All.First(t => t.Id == DefaultId);
}
