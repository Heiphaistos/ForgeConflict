using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ForgeConflict.App.ViewModels;
using ForgeConflict.Core.Rules;
using ForgeConflict.Core.Safety;

namespace ForgeConflict.App.Views;

public partial class ConflictsPage : Page
{
    public ConflictsPage(ShellViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();

        var report = viewModel.Rules;
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

    /// <summary>Presentation d'un constat, avec sa couleur de gravite resolue.</summary>
    private sealed class FindingCard
    {
        public FindingCard(Finding finding)
        {
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
        }

        public string Title { get; }
        public string Subtitle { get; }
        public string Evidence { get; }
        public string Explanation { get; }
        public string SuggestedFix { get; }
        public string RiskText { get; }
        public Brush SeverityBrush { get; }
        public Visibility FixVisibility { get; }

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
