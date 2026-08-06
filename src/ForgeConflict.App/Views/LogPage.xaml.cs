using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace ForgeConflict.App.Views;

public partial class LogPage : Page
{
    public LogPage()
    {
        InitializeComponent();
        Chemin.Text = AppPaths.LogDirectory;
        Load();
    }

    private void OnRefresh(object sender, RoutedEventArgs e) => Load();

    private void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        if (!Directory.Exists(AppPaths.LogDirectory)) return;

        Process.Start(new ProcessStartInfo
        {
            FileName = AppPaths.LogDirectory,
            UseShellExecute = true,
        });
    }

    private void Load()
    {
        var path = Path.Combine(AppPaths.LogDirectory, "forgeconflict.log");
        if (!File.Exists(path))
        {
            Contenu.Text = "Aucun journal pour cette session.";
            return;
        }

        try
        {
            // Le journal est ouvert en partage : l'application y ecrit pendant la lecture.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            Contenu.Text = reader.ReadToEnd();
        }
        catch (IOException ex)
        {
            Contenu.Text = $"Journal illisible : {ex.Message}";
        }
    }
}
