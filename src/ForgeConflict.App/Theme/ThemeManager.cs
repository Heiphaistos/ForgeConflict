using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using ForgeConflict.Core.Logging;

namespace ForgeConflict.App.Theme;

/// <summary>
/// Applique un theme et retient le choix. Les pinceaux sont remplaces en place dans
/// les ressources de l'application : les liaisons DynamicResource des vues se mettent
/// a jour sans qu'aucune page ait besoin d'etre rechargee.
/// </summary>
public sealed class ThemeManager
{
    private readonly ILog _log;
    private readonly string _settingsPath;

    public ThemeManager(ILog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        _log = log;
        _settingsPath = Path.Combine(AppPaths.BaseDirectory, "forgeconflict-settings.json");
    }

    public ThemeDefinition Current { get; private set; } = ThemeCatalog.Get(ThemeCatalog.DefaultId);

    public event EventHandler<ThemeDefinition>? Changed;

    public void Apply(ThemeDefinition theme)
    {
        ArgumentNullException.ThrowIfNull(theme);

        var resources = Application.Current.Resources;

        // Base claire ou sombre : sans cela, les controles natifs de WPF-UI garderaient
        // leur propre fond et jureraient avec les surfaces du theme.
        Wpf.Ui.Appearance.ApplicationThemeManager.Apply(
            theme.Base == ThemeBase.Dark
                ? Wpf.Ui.Appearance.ApplicationTheme.Dark
                : Wpf.Ui.Appearance.ApplicationTheme.Light,
            Wpf.Ui.Controls.WindowBackdropType.Mica,
            updateAccent: false);

        Set(resources, "Accent", theme.Accent);
        Set(resources, "Surface", theme.Surface);
        Set(resources, "SurfaceRaised", theme.SurfaceRaised);
        Set(resources, "BordureTheme", theme.Border);
        Set(resources, "TexteFort", theme.TextPrimary);
        Set(resources, "TexteDoux", theme.TextSecondary);

        Set(resources, "Sain", theme.Healthy);
        Set(resources, "Attention", theme.Warning);
        Set(resources, "Critique", theme.Critical);
        Set(resources, "Inconnu", theme.Unknown);

        Set(resources, "SainDoux", theme.Healthy, 0.16);
        Set(resources, "AttentionDoux", theme.Warning, 0.16);
        Set(resources, "CritiqueDoux", theme.Critical, 0.16);

        Current = theme;
        _log.Write(LogLevel.Info, $"theme applique : {theme.Id}");
        Changed?.Invoke(this, theme);
    }

    /// <summary>
    /// Remplace la couleur du pinceau existant plutot que le pinceau lui-meme :
    /// les references deja resolues par les vues continuent de pointer au bon endroit.
    /// </summary>
    private static void Set(ResourceDictionary resources, string key, Color color, double opacity = 1)
    {
        if (resources[key] is SolidColorBrush brush && !brush.IsFrozen)
        {
            brush.Color = color;
            brush.Opacity = opacity;
            return;
        }

        resources[key] = new SolidColorBrush(color) { Opacity = opacity };
    }

    public void LoadSaved()
    {
        Apply(ThemeCatalog.Get(ReadSavedId()));
    }

    public void Save(string themeId)
    {
        try
        {
            File.WriteAllText(_settingsPath, JsonSerializer.Serialize(new Settings(themeId)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Un exe portable peut vivre sur un support en lecture seule : le choix de
            // theme est alors valable pour la session, sans faire echouer l'application.
            _log.Write(LogLevel.Warn, $"preference de theme non enregistree : {ex.Message}");
        }
    }

    private string? ReadSavedId()
    {
        try
        {
            if (!File.Exists(_settingsPath)) return null;
            return JsonSerializer.Deserialize<Settings>(File.ReadAllText(_settingsPath))?.ThemeId;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _log.Write(LogLevel.Warn, $"preferences illisibles, theme par defaut : {ex.Message}");
            return null;
        }
    }

    private sealed record Settings(string ThemeId);
}
