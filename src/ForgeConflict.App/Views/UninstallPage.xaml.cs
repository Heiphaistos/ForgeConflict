using System.IO;
using System.Windows;
using System.Windows.Controls;
using ForgeConflict.App.ViewModels;
using ForgeConflict.Core.Inventory;
using ForgeConflict.Core.Logging;
using ForgeConflict.Core.Safety;
using ForgeConflict.Core.Uninstall;

namespace ForgeConflict.App.Views;

public partial class UninstallPage : Page
{
    private readonly ShellViewModel _viewModel;
    private readonly ILog _log;
    private readonly DriverUninstaller _uninstaller;

    public UninstallPage(ShellViewModel viewModel, ILog log)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(log);

        _viewModel = viewModel;
        _log = log;
        _uninstaller = new DriverUninstaller(
            Path.Combine(AppPaths.JournalDirectory, "uninstall.json"),
            new BootConfiguration(log),
            log);

        InitializeComponent();
        Refresh();
    }

    private void Refresh()
    {
        var pending = _uninstaller.Load();

        if (pending is { IsInProgress: true })
        {
            Reprise.Visibility = Visibility.Visible;
            RepriseDetail.Text =
                $"Une desinstallation du pilote {pending.Vendor} a ete engagee le " +
                $"{pending.StartedAt:g} et s'est arretee a l'etape « {pending.Phase} ». " +
                $"{pending.RemovedItems.Count} element(s) ont deja ete retires. " +
                "Vous pouvez la reprendre, ou l'abandonner pour revenir au mode normal.";
        }
        else
        {
            Reprise.Visibility = Visibility.Collapsed;
        }

        if (_viewModel.Snapshot is not { } snapshot ||
            !snapshot.Gpus.IsUsable || snapshot.Gpus.Value is not { } gpus)
        {
            Etat.Text = "Lancez une analyse depuis le tableau de bord pour lister les pilotes installes.";
            return;
        }

        var targets = gpus
            .Where(g => g.Vendor is GpuVendor.Nvidia or GpuVendor.Amd or GpuVendor.Intel)
            .Where(g => !g.IsGenericMicrosoftDriver)
            .Select(g => new TargetCard(g.Name, g.Vendor,
                $"pilote {g.DriverVersion ?? "inconnu"} — paquet {g.InfName ?? "inconnu"}"))
            .ToList();

        Cibles.ItemsSource = targets;

        Etat.Text = targets.Count == 0
            ? "Aucun pilote constructeur n'est installe : il n'y a rien a desinstaller."
            : string.Empty;
    }

    /// <summary>
    /// Execute une etape qui modifie le systeme hors du thread UI, comptee par
    /// <see cref="InstallTracker"/> pour que la fermeture ne la coupe jamais. La page
    /// est desactivee pendant ce temps pour empecher un second lancement.
    /// </summary>
    private async Task<T> RunTrackedAsync<T>(string name, Func<T> work)
    {
        using var operation = InstallTracker.Begin(name);
        IsEnabled = false;
        try
        {
            return await Task.Run(work).ConfigureAwait(true);
        }
        finally
        {
            IsEnabled = true;
        }
    }

    private async void OnPrepare(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: GpuVendor vendor }) return;
        if (_viewModel.Snapshot is not { } snapshot) return;

        var packages = snapshot.DriverPackages.IsUsable && snapshot.DriverPackages.Value is { } list
            ? list.Where(p => p.IsDisplayClass && MatchesVendor(p.ProviderName, vendor))
                  .Select(p => p.PublishedName).ToList()
            : [];

        var services = DriverUninstaller.ServicesFor(vendor);

        var summary =
            $"Pilote {vendor}\n\n" +
            $"Paquets a retirer : {(packages.Count == 0 ? "aucun" : string.Join(", ", packages))}\n" +
            $"Services a supprimer : {string.Join(", ", services)}\n" +
            $"Dossiers a effacer :\n  {string.Join("\n  ", DriverUninstaller.ResidualFolders(vendor))}\n\n" +
            "La machine va redemarrer en mode sans echec. Enregistrez votre travail avant de continuer.\n\n" +
            "Voulez-vous preparer la desinstallation ?";

        var answer = MessageBox.Show(summary, "Confirmer la desinstallation",
            MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);

        if (answer != MessageBoxResult.Yes)
        {
            Etat.Text = "Desinstallation annulee. Rien n'a ete modifie.";
            return;
        }

        var state = UninstallState.Start(vendor, packages, services);
        _uninstaller.Save(state);

        var prepared = await RunTrackedAsync($"La preparation de la desinstallation du pilote {vendor}",
            () => _uninstaller.Prepare(state));

        if (prepared.Failures.Count > 0)
        {
            // L'echec le plus probable est l'impossibilite d'armer la sortie de mode
            // sans echec. Dans ce cas, rien n'a ete engage : c'est voulu.
            Etat.Text = "Preparation interrompue : " + string.Join(" ", prepared.Failures) +
                        " Aucun redemarrage n'a ete programme.";
            _log.Write(LogLevel.Error, "preparation de desinstallation interrompue");
            return;
        }

        Etat.Text = "Preparation terminee. Redemarrez la machine pour lancer le nettoyage " +
                    "en mode sans echec.";
        Refresh();
    }

    private async void OnResume(object sender, RoutedEventArgs e)
    {
        var state = _uninstaller.Load();
        if (state is null) return;

        if (!BootConfiguration.IsInSafeMode() && state.Phase == UninstallPhase.SafeModeBooting)
        {
            Etat.Text = "Le nettoyage doit s'executer en mode sans echec. Redemarrez la machine : " +
                        "elle y entrera automatiquement.";
            return;
        }

        var report = await RunTrackedAsync($"Le nettoyage du pilote {state.Vendor}",
            () => _uninstaller.Finish(_uninstaller.Clean(state)));

        Etat.Text = $"Nettoyage termine : {report.RemovedItems.Count} element(s) retire(s), " +
                    $"{report.Failures.Count} echec(s). Redemarrez pour revenir au mode normal, " +
                    "puis installez le pilote de votre choix.";

        Refresh();
    }

    private void OnAbort(object sender, RoutedEventArgs e)
    {
        var boot = new BootConfiguration(_log);
        boot.ExitSafeMode();
        boot.DisarmSafeModeExit();
        _uninstaller.Clear();

        Etat.Text = "Desinstallation abandonnee. La machine redemarrera en mode normal.";
        Refresh();
    }

    private static bool MatchesVendor(string? provider, GpuVendor vendor)
    {
        if (string.IsNullOrWhiteSpace(provider)) return false;

        return vendor switch
        {
            GpuVendor.Nvidia => provider.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase),
            GpuVendor.Amd => provider.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
                             provider.Contains("Advanced Micro", StringComparison.OrdinalIgnoreCase),
            GpuVendor.Intel => provider.Contains("Intel", StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }

    private sealed record TargetCard(string AdapterName, GpuVendor Vendor, string Detail);
}
