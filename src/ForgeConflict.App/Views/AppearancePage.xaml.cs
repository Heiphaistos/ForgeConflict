using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ForgeConflict.App.Theme;

namespace ForgeConflict.App.Views;

public partial class AppearancePage : Page
{
    private readonly ThemeManager _themes;

    public AppearancePage(ThemeManager themes)
    {
        ArgumentNullException.ThrowIfNull(themes);

        _themes = themes;
        InitializeComponent();
        Bind();
    }

    private void Bind() =>
        Themes.ItemsSource = ThemeCatalog.All
            .Select(t => new ThemeCard(t, isCurrent: t.Id == _themes.Current.Id))
            .ToList();

    private void OnChoose(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id }) return;

        var theme = ThemeCatalog.Get(id);
        _themes.Apply(theme);
        _themes.Save(theme.Id);

        // Les apercus se lient a leurs propres couleurs figees : seule la marque
        // « actif » change, il faut donc reconstruire la liste.
        Bind();
    }

    /// <summary>
    /// Apercu d'un theme. Les pinceaux sont figes sur les couleurs du theme presente,
    /// et non lies aux ressources de l'application : chaque carte doit montrer SON
    /// theme, pas celui en cours.
    /// </summary>
    private sealed class ThemeCard
    {
        public ThemeCard(ThemeDefinition theme, bool isCurrent)
        {
            Id = theme.Id;
            Name = theme.Name;
            Description = theme.Description;

            AccentBrush = Freeze(theme.Accent);
            SurfaceBrush = Freeze(theme.Surface);
            SurfaceRaisedBrush = Freeze(theme.SurfaceRaised);
            BorderBrush = Freeze(isCurrent ? theme.Accent : theme.Border);
            TextPrimaryBrush = Freeze(theme.TextPrimary);
            TextSecondaryBrush = Freeze(theme.TextSecondary);
            HealthyBrush = Freeze(theme.Healthy);
            WarningBrush = Freeze(theme.Warning);
            CriticalBrush = Freeze(theme.Critical);

            SelectionVisibility = isCurrent ? Visibility.Visible : Visibility.Collapsed;
        }

        public string Id { get; }
        public string Name { get; }
        public string Description { get; }

        public Brush AccentBrush { get; }
        public Brush SurfaceBrush { get; }
        public Brush SurfaceRaisedBrush { get; }
        public Brush BorderBrush { get; }
        public Brush TextPrimaryBrush { get; }
        public Brush TextSecondaryBrush { get; }
        public Brush HealthyBrush { get; }
        public Brush WarningBrush { get; }
        public Brush CriticalBrush { get; }

        public Visibility SelectionVisibility { get; }

        private static Brush Freeze(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }
}
