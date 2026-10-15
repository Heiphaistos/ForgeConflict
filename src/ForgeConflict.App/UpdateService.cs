using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using ForgeConflict.App.Views;
using ForgeConflict.Core.Logging;
using ForgeConflict.Core.Update;

namespace ForgeConflict.App;

/// <summary>
/// Mises a jour cote application : verification silencieuse au demarrage puis toutes
/// les 6 h (si le reglage est actif), verification manuelle, dialogue d'installation.
/// Tout est asynchrone : l'interface n'est jamais bloquee par le reseau.
/// </summary>
public sealed class UpdateService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    private readonly ILog _log;
    private readonly HttpClient _http;
    private readonly AppUpdater _updater;
    private readonly DispatcherTimer _timer = new() { Interval = Interval };
    private bool _busy;

    public UpdateService(ILog log)
    {
        _log = log;
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("ForgeConflict/" + ThisAssembly.Version);
        _updater = new AppUpdater(_http, log);
        Settings = new UpdateSettings(Path.Combine(AppPaths.BaseDirectory, "forgeconflict-update.json"));
    }

    public UpdateSettings Settings { get; }

    /// <summary>Exe courant s'il peut etre remplace (pas une copie de developpement lancee via dotnet).</summary>
    public static string? InstallableExe =>
        string.Equals(Path.GetFileName(Environment.ProcessPath), "ForgeConflict.exe", StringComparison.OrdinalIgnoreCase)
            ? Environment.ProcessPath : null;

    public void StartBackgroundChecks()
    {
        _timer.Tick += async (_, _) => { if (Settings.AutoCheck) await CheckAsync(silent: true); };
        _timer.Start();
        if (Settings.AutoCheck) _ = CheckAsync(silent: true);
    }

    /// <summary>Renvoie un texte d'etat en francais pour la page ; silencieux = aucune boite de dialogue en l'absence de nouveaute.</summary>
    public async Task<string> CheckAsync(bool silent)
    {
        if (_busy) return "Une vérification est déjà en cours.";
        _busy = true;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var info = await _updater.CheckAsync(ThisAssembly.Version, cts.Token);
            if (info is null)
            {
                _log.Write(LogLevel.Info, "mise a jour : version a jour");
                return $"Vous utilisez la dernière version ({ThisAssembly.Version}).";
            }

            _log.Write(LogLevel.Info, $"mise a jour disponible : {info.Version}");
            var exe = InstallableExe;
            if (exe is null)
                return $"La version {info.Version} existe, mais cette copie de développement ne peut pas se remplacer elle-même.";

            Show(info, exe);
            return $"La version {info.Version} est disponible.";
        }
        catch (Exception ex) when (ex is UpdateException or OperationCanceledException)
        {
            var message = ex is UpdateException ? ex.Message : "Le serveur de mises à jour ne répond pas (délai dépassé).";
            _log.Write(LogLevel.Warn, "verification de mise a jour impossible : " + message);
            return silent ? "" : message;
        }
        finally
        {
            _busy = false;
        }
    }

    private static void Show(UpdateInfo info, string exe)
    {
        var owner = Application.Current.MainWindow;
        var window = new UpdateWindow(info, exe) { Owner = owner };
        window.ShowDialog();
    }

    /// <summary>Telecharge, controle et lance le remplacement ; l'appelant ferme l'application ensuite.</summary>
    public async Task InstallAsync(UpdateInfo info, string exe, IProgress<double> progress)
    {
        var path = await _updater.DownloadAsync(info, exe, progress);
        _log.Write(LogLevel.Info, $"mise a jour {info.Version} telechargee et verifiee, remplacement en cours");
        AppUpdater.LaunchReplacement(path, exe);
    }
}
