using System.Diagnostics;
using System.Text;
using ForgeConflict.Core.Inventory;
using ForgeConflict.Core.Logging;

namespace ForgeConflict.Core.Uninstall;

/// <summary>
/// Desinstallation complete d'un pilote graphique, dans l'esprit de DDU.
///
/// La machine a etats est persistee a chaque transition : l'operation traverse
/// deux redemarrages, et chaque etape doit pouvoir reprendre la ou la precedente
/// s'est arretee, y compris apres une coupure.
/// </summary>
public sealed class DriverUninstaller
{
    private readonly string _statePath;
    private readonly BootConfiguration _boot;
    private readonly ILog _log;

    public DriverUninstaller(string statePath, BootConfiguration boot, ILog log)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(statePath);
        ArgumentNullException.ThrowIfNull(boot);
        ArgumentNullException.ThrowIfNull(log);

        _statePath = statePath;
        _boot = boot;
        _log = log;
    }

    /// <summary>Dossiers residuels laisses par les pilotes constructeurs.</summary>
    public static IReadOnlyList<string> ResidualFolders(GpuVendor vendor) => vendor switch
    {
        GpuVendor.Nvidia =>
        [
            @"C:\NVIDIA",
            @"C:\Program Files\NVIDIA Corporation",
            @"C:\Program Files (x86)\NVIDIA Corporation",
            @"C:\ProgramData\NVIDIA",
            @"C:\ProgramData\NVIDIA Corporation",
        ],
        GpuVendor.Amd =>
        [
            @"C:\AMD",
            @"C:\Program Files\AMD",
            @"C:\Program Files (x86)\AMD",
            @"C:\ProgramData\AMD",
        ],
        GpuVendor.Intel =>
        [
            @"C:\Program Files\Intel\Intel(R) Graphics Driver",
            @"C:\ProgramData\Intel",
        ],
        _ => [],
    };

    public static IReadOnlyList<string> ServicesFor(GpuVendor vendor) => vendor switch
    {
        GpuVendor.Nvidia => ["nvlddmkm", "nvagent", "NVDisplay.ContainerLocalSystem", "NvContainerLocalSystem", "nvvad"],
        GpuVendor.Amd => ["amdkmdag", "amdwddmg", "AMD External Events Utility", "amdlog"],
        GpuVendor.Intel => ["igfxCUIService", "IntelGraphicsSoftwareService", "igfxn"],
        _ => [],
    };

    public UninstallState? Load()
    {
        try
        {
            return File.Exists(_statePath) ? UninstallState.FromJson(File.ReadAllText(_statePath)) : null;
        }
        catch (IOException ex)
        {
            _log.Write(LogLevel.Error, "etat de desinstallation illisible", ex);
            return null;
        }
    }

    public void Save(UninstallState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var directory = Path.GetDirectoryName(_statePath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

        var temporary = _statePath + ".tmp";
        File.WriteAllText(temporary, state.ToJson(), Encoding.UTF8);
        File.Move(temporary, _statePath, overwrite: true);
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(_statePath)) File.Delete(_statePath);
        }
        catch (IOException ex)
        {
            _log.Write(LogLevel.Warn, $"etat de desinstallation non efface : {ex.Message}");
        }
    }

    /// <summary>
    /// Prepare l'operation : bloque la reinstallation par Windows Update, arme la
    /// sortie de mode sans echec, puis demande l'entree en mode sans echec.
    ///
    /// L'ordre est imperatif et verifie : si l'armement echoue, l'entree en mode
    /// sans echec n'a PAS lieu.
    /// </summary>
    public UninstallState Prepare(UninstallState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var blocked = BlockWindowsUpdateDrivers();
        var armed = _boot.ArmSafeModeExit();

        var prepared = state with
        {
            Phase = UninstallPhase.Prepared,
            WindowsUpdateBlocked = blocked,
            SafeBootExitArmed = armed,
        };

        Save(prepared);

        if (!armed)
        {
            _log.Write(LogLevel.Error,
                "preparation interrompue : sans sortie de mode sans echec garantie, l'operation " +
                "risquerait de laisser la machine bloquee");

            return prepared.Failed("Sortie de mode sans echec impossible a armer.");
        }

        if (!_boot.EnterSafeMode(exitArmed: true))
        {
            return prepared.Failed("Entree en mode sans echec impossible.");
        }

        var booting = prepared.With(UninstallPhase.SafeModeBooting);
        Save(booting);
        return booting;
    }

    /// <summary>Nettoyage proprement dit, execute en mode sans echec.</summary>
    public UninstallState Clean(UninstallState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var current = state.With(UninstallPhase.Cleaning);
        Save(current);

        foreach (var inf in state.TargetInfPackages)
        {
            var (code, output) = Run("pnputil.exe", "/delete-driver", inf, "/uninstall", "/force");

            current = code == 0
                ? current.Removed($"paquet {inf}")
                : current.Failed($"paquet {inf} : {FirstLine(output)}");

            Save(current);
        }

        foreach (var service in state.TargetServices)
        {
            var (code, _) = Run("sc.exe", "delete", service);

            // Le code 1060 signifie « service inexistant » : c'est le resultat vise,
            // pas un echec.
            current = code is 0 or 1060
                ? current.Removed($"service {service}")
                : current.Failed($"service {service} (code {code})");

            Save(current);
        }

        foreach (var folder in ResidualFolders(state.Vendor))
        {
            if (!Directory.Exists(folder)) continue;

            try
            {
                Directory.Delete(folder, recursive: true);
                current = current.Removed($"dossier {folder}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                current = current.Failed($"dossier {folder} : {ex.Message}");
            }

            Save(current);
        }

        var finished = current.With(UninstallPhase.NormalBooting);
        Save(finished);

        _boot.ExitSafeMode();
        _boot.DisarmSafeModeExit();

        return finished;
    }

    /// <summary>Derniere etape : retablit Windows Update et clot l'operation.</summary>
    public UninstallState Finish(UninstallState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.WindowsUpdateBlocked) RestoreWindowsUpdateDrivers();

        _boot.ExitSafeMode();
        _boot.DisarmSafeModeExit();

        var report = state.With(UninstallPhase.Report);
        Save(report);

        _log.Write(LogLevel.Info,
            $"desinstallation terminee : {report.RemovedItems.Count} element(s) retire(s), " +
            $"{report.Failures.Count} echec(s)");

        return report;
    }

    /// <summary>
    /// Empeche Windows Update de reinstaller le pilote pendant l'operation. Sans ce
    /// blocage, Windows peut remettre un pilote en place entre le nettoyage et
    /// l'installation choisie par l'utilisateur.
    /// </summary>
    private bool BlockWindowsUpdateDrivers()
    {
        var (code, _) = Run("reg.exe", "add",
            @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\DriverSearching",
            "/v", "SearchOrderConfig", "/t", "REG_DWORD", "/d", "0", "/f");

        if (code == 0) _log.Write(LogLevel.Info, "recherche de pilotes par Windows Update suspendue");
        return code == 0;
    }

    private void RestoreWindowsUpdateDrivers()
    {
        var (code, _) = Run("reg.exe", "add",
            @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\DriverSearching",
            "/v", "SearchOrderConfig", "/t", "REG_DWORD", "/d", "1", "/f");

        if (code == 0) _log.Write(LogLevel.Info, "recherche de pilotes par Windows Update retablie");
    }

    private static (int Code, string Output) Run(string file, params string[] arguments)
    {
        var info = new ProcessStartInfo
        {
            FileName = file,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
        };

        foreach (var argument in arguments) info.ArgumentList.Add(argument);

        using var process = Process.Start(info);
        if (process is null) return (-1, "processus non lance");

        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();

        if (!process.WaitForExit(180_000))
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            return (-1, "delai depasse");
        }

        return (process.ExitCode, output);
    }

    private static string FirstLine(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? string.Empty;
}
