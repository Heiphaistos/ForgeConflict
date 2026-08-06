using System.Windows;
using System.Windows.Media;
using ForgeConflict.Core.Remediation;
using ForgeConflict.Core.Safety;
using Wpf.Ui.Controls;

namespace ForgeConflict.App.Views;

/// <summary>
/// Montre l'apercu exact d'une correction et recueille l'accord de l'utilisateur.
/// Rien n'est applique tant que cette fenetre n'a pas rendu vrai.
/// </summary>
public partial class ConfirmChangesWindow : FluentWindow
{
    public ConfirmChangesWindow(IRemediation remediation, IReadOnlyList<ChangeRecord> changes)
    {
        ArgumentNullException.ThrowIfNull(remediation);
        ArgumentNullException.ThrowIfNull(changes);

        InitializeComponent();

        Titre.Text = remediation.Title;
        Explication.Text = remediation.Explanation;
        Changements.ItemsSource = changes.Select(c => new ChangeLine(c)).ToList();

        ShowRisk(remediation);
    }

    public bool Accepted { get; private set; }

    private void ShowRisk(IRemediation remediation)
    {
        var (title, detail, brushKey) = remediation.Risk switch
        {
            RiskLevel.Faible => (
                "Risque faible",
                "Cette modification est sans consequence sur l'affichage et se defait a tout moment.",
                "Sain"),
            RiskLevel.Moyen => (
                "Risque moyen",
                "Cette modification touche a la configuration du systeme. Elle est enregistree avant " +
                "d'etre appliquee et peut etre annulee.",
                "Attention"),
            RiskLevel.Eleve => (
                "Risque eleve",
                "Cette modification touche aux reglages du pilote graphique. L'etat actuel est " +
                "enregistre sur disque avant toute ecriture : si l'affichage se degrade ou si la " +
                "machine s'interrompt, l'etat precedent sera retabli automatiquement au demarrage suivant.",
                "Critique"),
            _ => (
                "Risque critique",
                "Cette modification peut priver la machine d'affichage. Un point de restauration et " +
                "une sauvegarde complete sont realises avant d'agir.",
                "Critique"),
        };

        TitreRisque.Text = title;
        DetailRisque.Text = remediation.RequiresReboot
            ? detail + " Un redemarrage sera necessaire pour que le changement prenne effet."
            : detail;

        var brush = (Brush?)TryFindResource(brushKey) ?? Brushes.Gray;
        BarreRisque.Fill = brush;
        BandeauRisque.BorderBrush = brush;
    }

    private void OnApply(object sender, RoutedEventArgs e)
    {
        Accepted = true;
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        Accepted = false;
        DialogResult = false;
    }

    /// <summary>Une ligne d'apercu : cible, nom, valeur avant, valeur apres.</summary>
    private sealed class ChangeLine
    {
        public ChangeLine(ChangeRecord change)
        {
            Target = change.Target;
            ValueName = change.ValueName ?? change.Target;

            Before = change.PreviousValueExisted
                ? $"{change.PreviousValue}"
                : "(absente)";

            // Une valeur retiree est presentee comme telle, avec sa consequence :
            // « supprimee » seul laisserait croire a une perte, alors que c'est un
            // retour au comportement par defaut de Windows.
            After = change.NewValue is null
                ? "supprimee (defaut Windows)"
                : $"{change.NewValue}";
        }

        public string Target { get; }
        public string ValueName { get; }
        public string Before { get; }
        public string After { get; }
    }
}
