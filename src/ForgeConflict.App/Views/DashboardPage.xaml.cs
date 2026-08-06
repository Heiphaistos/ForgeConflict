using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ForgeConflict.App.ViewModels;
using ForgeConflict.Core.Inventory;
using ForgeConflict.Core.Rules;

namespace ForgeConflict.App.Views;

public partial class DashboardPage : Page
{
    private readonly ShellViewModel _viewModel;

    public DashboardPage(ShellViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        _viewModel = viewModel;
        InitializeComponent();
        Refresh();
    }

    private async void OnScan(object sender, RoutedEventArgs e)
    {
        ScanButton.IsEnabled = false;
        Sous.Text = "Analyse en cours...";
        try
        {
            await _viewModel.ScanAsync();
        }
        finally
        {
            ScanButton.IsEnabled = true;
            Refresh();
        }
    }

    private void Refresh()
    {
        SourcesListe.ItemsSource = _viewModel.Sources;
        Ring.Segments = _viewModel.Sources.Select(s => s.Level).ToList();

        var total = _viewModel.Sources.Count;
        var saines = _viewModel.Sources.Count(s => s.Level == HealthLevel.Sain);

        EtatGlobal.Text = _viewModel.Overall switch
        {
            HealthLevel.Sain => "Sain",
            HealthLevel.Attention => "Partiel",
            HealthLevel.Critique => "Probleme",
            _ => "-",
        };

        Compte.Text = $"{saines} / {total} sources";

        if (_viewModel.Snapshot is { } snapshot)
        {
            Sous.Text = $"{snapshot.MachineName} — {snapshot.OsVersion}";
            Horodatage.Text = $"Derniere analyse : {snapshot.CapturedAt:g}";
        }

        RefreshVerdict();
    }

    private void RefreshVerdict()
    {
        if (_viewModel.Rules is not { } report)
        {
            Verdict.Visibility = Visibility.Collapsed;
            return;
        }

        Verdict.Visibility = Visibility.Visible;

        var problems = report.Problems.Count;
        var infos = report.Findings.Count - problems;

        VerdictTitre.Text = problems switch
        {
            0 => "Aucun conflit detecte",
            1 => "1 conflit detecte",
            _ => $"{problems} conflits detectes",
        };

        VerdictDetail.Text = infos > 0
            ? $"{infos} information(s) complementaire(s) — voir la page Conflits"
            : "Voir la page Conflits pour le detail";

        var key = report.Problems.Count == 0
            ? "Sain"
            : report.Problems.Max(f => f.Severity) >= Severity.Eleve ? "Critique" : "Attention";

        VerdictBarre.Fill = (Brush?)TryFindResource(key) ?? Brushes.Gray;
    }
}
