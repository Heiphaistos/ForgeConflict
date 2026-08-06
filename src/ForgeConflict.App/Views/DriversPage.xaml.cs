using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ForgeConflict.App.ViewModels;
using ForgeConflict.Core.Catalog;

namespace ForgeConflict.App.Views;

public partial class DriversPage : Page
{
    private readonly ShellViewModel _viewModel;
    private readonly DriverCatalog _catalog = DriverCatalog.Embedded();

    public DriversPage(ShellViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        _viewModel = viewModel;
        InitializeComponent();
        Refresh();
    }

    private void OnProfileChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded) Refresh();
    }

    private void Refresh()
    {
        if (_viewModel.Snapshot is not { } snapshot)
        {
            Sous.Text = "Lancez une analyse depuis le tableau de bord pour obtenir un conseil.";
            return;
        }

        var profile = Profil.SelectedIndex switch
        {
            1 => UsageProfile.Creation,
            2 => UsageProfile.Bureautique,
            _ => UsageProfile.Jeu,
        };

        var advices = new DriverAdvisor(_catalog).Advise(snapshot, profile);
        Conseils.ItemsSource = advices.Select(a => new AdviceCard(a)).ToList();

        Sous.Text = advices.Count == 0
            ? "Aucun adaptateur ne peut recevoir de conseil."
            : $"Conseil etabli pour {advices.Count} adaptateur(s), d'apres votre materiel, " +
              "la topologie de la machine et son historique d'incidents.";

        // L'age du catalogue est dit franchement : un conseil vieux de six mois ne
        // vaut pas un conseil recent, et l'utilisateur doit pouvoir en juger.
        AgeCatalogue.Text = _catalog.IsStale
            ? $"Attention : le catalogue embarque date du {_catalog.UpdatedAt:d} " +
              $"({_catalog.AgeInDays} jours). Des versions plus recentes existent probablement ; " +
              "verifiez sur le site du constructeur."
            : $"Catalogue embarque du {_catalog.UpdatedAt:d} ({_catalog.AgeInDays} jours).";
    }

    private void OnDownload(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string url } || string.IsNullOrWhiteSpace(url)) return;

        Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
    }

    private sealed class AdviceCard
    {
        public AdviceCard(DriverAdvice advice)
        {
            AdapterName = advice.AdapterName;
            Reasoning = advice.Reasoning;

            InstalledText = advice.InstalledVersion ?? "version inconnue";

            RecommendedText = advice.Recommended is { } release
                ? $"{release.Version}  ({release.Branch})"
                : "aucune recommandation";

            Issues = advice.IssuesOnInstalled
                .Select(i => $"• {i.Description}" +
                             (i.AffectedConfigurations is { } c ? $" ({c})" : string.Empty))
                .ToList();

            IssuesVisibility = Issues.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            UpToDateVisibility = advice.IsUpToDate && advice.Recommended is not null
                ? Visibility.Visible
                : Visibility.Collapsed;

            DownloadUrl = advice.Recommended?.DownloadUrl ?? string.Empty;
            DownloadVisibility = string.IsNullOrWhiteSpace(DownloadUrl) || advice.IsUpToDate
                ? Visibility.Collapsed
                : Visibility.Visible;

            var key = advice.Recommended is null ? "Inconnu"
                : advice.IsUpToDate ? "Sain"
                : "Attention";

            VerdictBrush = Application.Current.TryFindResource(key) as Brush ?? Brushes.Gray;
        }

        public string AdapterName { get; }
        public string InstalledText { get; }
        public string RecommendedText { get; }
        public string Reasoning { get; }
        public string DownloadUrl { get; }
        public IReadOnlyList<string> Issues { get; }
        public Brush VerdictBrush { get; }
        public Visibility IssuesVisibility { get; }
        public Visibility UpToDateVisibility { get; }
        public Visibility DownloadVisibility { get; }
    }
}
