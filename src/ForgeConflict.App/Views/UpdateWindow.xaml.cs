using System.Windows;
using ForgeConflict.Core.Update;
using Wpf.Ui.Controls;

namespace ForgeConflict.App.Views;

/// <summary>Annonce une mise a jour (version, notes) et l'installe avec une barre de progression.</summary>
public partial class UpdateWindow : FluentWindow
{
    private readonly UpdateInfo _info;
    private readonly string _exe;

    public UpdateWindow(UpdateInfo info, string exe)
    {
        _info = info;
        _exe = exe;
        InitializeComponent();
        Titre.Text = $"ForgeConflict {info.Version} est disponible (version actuelle : {ThisAssembly.Version})";
        Notes.Text = string.IsNullOrWhiteSpace(info.Notes) ? "Aucune note de version fournie." : info.Notes;
    }

    private void OnLater(object sender, RoutedEventArgs e) => Close();

    private async void OnInstall(object sender, RoutedEventArgs e)
    {
        Installer.IsEnabled = false;
        Plus_tard.IsEnabled = false;
        Progression.Visibility = Visibility.Visible;
        Etat.Text = "Téléchargement et vérification en cours…";
        try
        {
            var service = ((App)Application.Current).Updates;
            await service.InstallAsync(_info, _exe, new Progress<double>(p => Progression.Value = p));
            Etat.Text = "Mise à jour prête, redémarrage…";
            Application.Current.Shutdown();
        }
        catch (UpdateException ex)
        {
            Etat.Text = "Échec : " + ex.Message;
            Installer.IsEnabled = true;
            Plus_tard.IsEnabled = true;
            Progression.Visibility = Visibility.Collapsed;
        }
    }
}
