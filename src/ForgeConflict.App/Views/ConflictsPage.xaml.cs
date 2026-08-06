using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ForgeConflict.App.ViewModels;
using ForgeConflict.Core.Remediation;
using ForgeConflict.Core.Rules;
using ForgeConflict.Core.Safety;

namespace ForgeConflict.App.Views;

public partial class ConflictsPage : Page
{
    private readonly ShellViewModel _viewModel;

    public ConflictsPage(ShellViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        _viewModel = viewModel;
        InitializeComponent();
        Refresh();
    }

    private void Refresh()
    {
        var report = _viewModel.Rules;

        if (report is null)
        {
            RienATrouver.Visibility = Visibility.Collapsed;
            return;
        }

        Constats.ItemsSource = report.Findings.Select(f => new FindingCard(f)).ToList();

        Resume.Text = report.Findings.Count == 0
            ? "Aucun constat sur cette machine."
            : $"{report.Problems.Count} probleme(s) et " +
              $"{report.Findings.Count - report.Problems.Count} information(s).";

        if (report.FailedRules.Count > 0)
        {
            Resume.Text += $" {report.FailedRules.Count} regle(s) n'ont pas pu etre evaluees.";
        }

        RienATrouver.Visibility = report.Findings.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void OnRemediate(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string ruleId } button) return;
        if (_viewModel.Rules is not { } report || _viewModel.Snapshot is not { } snapshot) return;

        var finding = report.Findings.FirstOrDefault(f => f.RuleId == ruleId);
        if (finding is null) return;

        var remediation = RemediationService.For(finding);
        if (remediation is null) return;

        button.IsEnabled = false;
        try
        {
            var result = await _viewModel.ApplyAsync(remediation, snapshot, Confirm, ConfirmDisplay);

            Resume.Text = result.Message;

            // Le constat corrige peut avoir disparu : une nouvelle analyse le dira.
            if (result.Outcome == RemediationOutcome.Appliquee)
            {
                Resume.Text += " Relancez une analyse pour verifier le resultat.";
            }
        }
        finally
        {
            button.IsEnabled = true;
        }

        Task<bool> Confirm(IReadOnlyList<ChangeRecord> changes)
        {
            var dialog = new ConfirmChangesWindow(remediation, changes)
            {
                Owner = Window.GetWindow(this),
            };

            dialog.ShowDialog();
            return Task.FromResult(dialog.Accepted);
        }

        Task<bool> ConfirmDisplay()
        {
            var dialog = new WatchdogWindow(DisplayWatchdog.DefaultDelay);
            dialog.Show();
            return dialog.Answer;
        }
    }

    /// <summary>Presentation d'un constat, avec sa couleur de gravite resolue.</summary>
    private sealed class FindingCard
    {
        public FindingCard(Finding finding)
        {
            RuleId = finding.RuleId;
            Title = finding.Title;
            Evidence = finding.Evidence;
            Explanation = finding.Explanation;
            SuggestedFix = finding.SuggestedFix ?? string.Empty;

            Subtitle = $"{Describe(finding.Severity)} · {finding.RuleId}";
            SeverityBrush = BrushFor(finding.Severity);
            FixVisibility = finding.HasFix ? Visibility.Visible : Visibility.Collapsed;

            RiskText = finding.FixRisk switch
            {
                RiskLevel.Faible => "Risque de la correction : faible.",
                RiskLevel.Moyen => "Risque de la correction : moyen.",
                RiskLevel.Eleve => "Risque de la correction : eleve — sauvegarde et confirmation exigees.",
                RiskLevel.Critique => "Risque de la correction : critique — point de restauration exige.",
                _ => string.Empty,
            };

            // Une correction proposee n'est pas forcement automatisable : le dire
            // franchement vaut mieux qu'un bouton absent sans explication.
            var automated = RemediationService.For(finding) is not null;
            ActionVisibility = automated ? Visibility.Visible : Visibility.Collapsed;
            ManualOnlyVisibility = finding.HasFix && !automated ? Visibility.Visible : Visibility.Collapsed;
            ManualOnlyText = "Cette correction n'est pas encore automatisee : elle doit etre realisee " +
                             "manuellement en suivant la description ci-dessus.";
        }

        public string RuleId { get; }
        public string Title { get; }
        public string Subtitle { get; }
        public string Evidence { get; }
        public string Explanation { get; }
        public string SuggestedFix { get; }
        public string RiskText { get; }
        public string ManualOnlyText { get; }
        public Brush SeverityBrush { get; }
        public Visibility FixVisibility { get; }
        public Visibility ActionVisibility { get; }
        public Visibility ManualOnlyVisibility { get; }

        private static string Describe(Severity severity) => severity switch
        {
            Severity.Critique => "Critique",
            Severity.Eleve => "Eleve",
            Severity.Moyen => "Moyen",
            _ => "Information",
        };

        private static Brush BrushFor(Severity severity)
        {
            var key = severity switch
            {
                Severity.Critique or Severity.Eleve => "Critique",
                Severity.Moyen => "Attention",
                _ => "Inconnu",
            };

            return Application.Current.TryFindResource(key) as Brush ?? Brushes.Gray;
        }
    }
}
