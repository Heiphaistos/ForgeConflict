using System.Windows.Media;
using ForgeConflict.App.Theme;

namespace ForgeConflict.Tests.Theme;

public sealed class ThemeCatalogTests
{
    /// <summary>
    /// Luminance relative WCAG. Sert a calculer un rapport de contraste reel plutot
    /// que de juger les couleurs a l'oeil.
    /// </summary>
    private static double Luminance(Color c)
    {
        static double Channel(byte v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(c.R)) + (0.7152 * Channel(c.G)) + (0.0722 * Channel(c.B));
    }

    private static double Contrast(Color a, Color b)
    {
        var la = Luminance(a);
        var lb = Luminance(b);
        var (hi, lo) = la > lb ? (la, lb) : (lb, la);
        return (hi + 0.05) / (lo + 0.05);
    }

    [Fact]
    public void All_ContainsTenDistinctThemes()
    {
        Assert.Equal(10, ThemeCatalog.All.Count);
        Assert.Equal(10, ThemeCatalog.All.Select(t => t.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(ThemeCatalog.All, t => Assert.False(string.IsNullOrWhiteSpace(t.Name)));
        Assert.All(ThemeCatalog.All, t => Assert.False(string.IsNullOrWhiteSpace(t.Description)));
    }

    [Fact]
    public void EveryTheme_KeepsStateColoursReadableOnItsOwnSurface()
    {
        // Une couleur d'etat illisible sur son propre fond ruinerait le seul signal
        // qui compte. 3:1 est le seuil WCAG pour un element graphique non textuel.
        foreach (var theme in ThemeCatalog.All)
        {
            foreach (var (nom, couleur) in new[]
                     {
                         ("sain", theme.Healthy),
                         ("attention", theme.Warning),
                         ("critique", theme.Critical),
                     })
            {
                var ratio = Contrast(couleur, theme.SurfaceRaised);
                Assert.True(ratio >= 3.0,
                    $"theme {theme.Id} : la couleur {nom} n'a qu'un contraste de {ratio:0.00} sur sa surface");
            }
        }
    }

    [Fact]
    public void EveryTheme_KeepsPrimaryTextComfortablyReadable()
    {
        // 4.5:1 est le seuil WCAG AA pour du texte de taille normale.
        foreach (var theme in ThemeCatalog.All)
        {
            var ratio = Contrast(theme.TextPrimary, theme.Surface);
            Assert.True(ratio >= 4.5,
                $"theme {theme.Id} : le texte principal n'a qu'un contraste de {ratio:0.00}");
        }
    }

    [Fact]
    public void EveryTheme_KeepsSecondaryTextLegible()
    {
        // 3:1 minimum pour le texte secondaire, plus petit mais toujours a lire.
        foreach (var theme in ThemeCatalog.All)
        {
            var ratio = Contrast(theme.TextSecondary, theme.Surface);
            Assert.True(ratio >= 3.0,
                $"theme {theme.Id} : le texte secondaire n'a qu'un contraste de {ratio:0.00}");
        }
    }

    [Fact]
    public void EveryTheme_KeepsHealthyAndWarningDistinguishable()
    {
        // Vert et ambre voisins seraient confondus par un daltonien deutan : ils
        // doivent differer nettement en luminance, pas seulement en teinte.
        foreach (var theme in ThemeCatalog.All)
        {
            var ecart = Math.Abs(Luminance(theme.Healthy) - Luminance(theme.Warning));
            Assert.True(ecart >= 0.05,
                $"theme {theme.Id} : sain et attention ne different que de {ecart:0.000} en luminance");
        }
    }

    [Fact]
    public void Get_FallsBackToTheDefaultForAnUnknownId()
    {
        Assert.Equal(ThemeCatalog.DefaultId, ThemeCatalog.Get("inexistant").Id);
        Assert.Equal(ThemeCatalog.DefaultId, ThemeCatalog.Get(null).Id);
    }

    [Fact]
    public void Get_IsCaseInsensitive() =>
        Assert.Equal("nocturne", ThemeCatalog.Get("NOCTURNE").Id);

    [Fact]
    public void All_OffersBothLightAndDarkBases()
    {
        Assert.Contains(ThemeCatalog.All, t => t.Base == ThemeBase.Light);
        Assert.Contains(ThemeCatalog.All, t => t.Base == ThemeBase.Dark);
    }
}
