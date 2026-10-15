using System.Windows;
using System.Windows.Controls;

namespace ForgeConflict.App.Views;

public partial class UpdatesPage : Page
{
    private readonly UpdateService _updates;

    public UpdatesPage(UpdateService updates)
    {
        ArgumentNullException.ThrowIfNull(updates);
        _updates = updates;
        InitializeComponent();
        Version.Text = $"Version installée : {ThisAssembly.Version}";
        Auto.IsChecked = updates.Settings.AutoCheck;
    }

    private async void OnCheck(object sender, RoutedEventArgs e)
    {
        Rechercher.IsEnabled = false;
        Etat.Text = "Recherche en cours…";
        Etat.Text = await _updates.CheckAsync(silent: false);
        Rechercher.IsEnabled = true;
    }

    private void OnAutoChanged(object sender, RoutedEventArgs e)
    {
        // Le Checked initial (IsChecked fixe dans le constructeur) ne doit pas reecrire le fichier.
        if (!IsLoaded) return;
        var saved = _updates.Settings.SetAutoCheck(Auto.IsChecked == true);
        if (!saved) Etat.Text = "Réglage valable pour cette session seulement (dossier en lecture seule).";
    }
}
