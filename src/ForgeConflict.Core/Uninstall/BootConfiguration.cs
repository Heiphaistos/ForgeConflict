using System.Diagnostics;
using System.IO;
using System.Text;
using ForgeConflict.Core.Logging;
using Microsoft.Win32;

namespace ForgeConflict.Core.Uninstall;

/// <summary>
/// Pilote la configuration de demarrage de Windows.
///
/// La regle absolue de ce type : la SORTIE du mode sans echec est armee AVANT
/// d'y entrer. Sans cela, un nettoyage qui echoue laisse la machine en boucle de
/// mode sans echec, sans moyen simple d'en sortir. C'est le pire scenario du
/// projet entier.
/// </summary>
public sealed class BootConfiguration
{
    private const string TaskName = "ForgeConflict-SortieModeSansEchec";

    private const string RunOncePath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce";

    /// <summary>
    /// L'etoile initiale n'est pas decorative : elle demande a Windows d'executer
    /// cette entree meme en mode sans echec. Sans elle, rien ne s'execute et la
    /// machine reste bloquee.
    /// </summary>
    private const string RunOnceName = "*ForgeConflictSortieModeSansEchec";

    private readonly ILog _log;

    public BootConfiguration(ILog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        _log = log;
    }

    /// <summary>
    /// Inscrit une tache de demarrage qui retire l'indicateur de mode sans echec.
    /// A appeler IMPERATIVEMENT avant <see cref="EnterSafeMode"/>.
    /// </summary>
    /// <summary>
    /// Arme DEUX mecanismes de sortie independants, car aucun ne suffit seul.
    ///
    /// Le planificateur de taches ne demarre PAS en mode sans echec : une tache
    /// « onstart » n'y sera jamais executee. Constate en machine virtuelle le
    /// 06/08/2026, ou la machine est restee bloquee en boucle de mode sans echec
    /// avec une tache correctement creee mais jamais declenchee.
    ///
    /// Le second mecanisme est une entree RunOnce dont le nom commence par une
    /// etoile : Windows documente que ce prefixe force son execution meme en mode
    /// sans echec. C'est celui qui sauve reellement la machine.
    /// </summary>
    public bool ArmSafeModeExit()
    {
        string scriptPath;

        try
        {
            scriptPath = WriteExitScript();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Write(LogLevel.Error, "script de sortie de mode sans echec non ecrit", ex);
            return false;
        }

        var runOnceArmed = ArmRunOnce(scriptPath);
        var taskArmed = ArmScheduledTask(scriptPath);

        // Le RunOnce est le mecanisme decisif : sans lui, la sortie n'est pas
        // garantie et l'entree en mode sans echec doit etre refusee.
        if (!runOnceArmed)
        {
            _log.Write(LogLevel.Error,
                "sortie de mode sans echec NON garantie : l'entree RunOnce n'a pas ete posee");
            return false;
        }

        _log.Write(LogLevel.Info,
            $"sortie de mode sans echec armee (RunOnce{(taskArmed ? " + tache planifiee" : " seul")})");

        return true;
    }

    /// <summary>
    /// Pose une entree RunOnce prefixee par une etoile. Ce prefixe est la seule
    /// facon documentee de faire executer une commande au demarrage en mode sans
    /// echec.
    /// </summary>
    private bool ArmRunOnce(string scriptPath)
    {
        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(RunOncePath, writable: true);
            if (key is null) return false;

            key.SetValue(RunOnceName, $"\"{scriptPath}\"", RegistryValueKind.String);

            // Relecture : poser sans verifier reviendrait a se fier a un filet
            // qu'on n'a jamais regarde.
            return key.GetValue(RunOnceName) is not null;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            _log.Write(LogLevel.Error, "entree RunOnce de sortie non posee", ex);
            return false;
        }
    }

    private bool ArmScheduledTask(string scriptPath)
    {
        // La commande passe par un fichier de commandes : schtasks rejette une
        // commande /tr contenant guillemets et esperluette, et la tache n'est
        // alors pas creee du tout.
        var (code, output) = Run("schtasks",
            "/create", "/tn", TaskName,
            "/tr", scriptPath,
            "/sc", "onstart",
            "/ru", "SYSTEM",
            "/rl", "HIGHEST",
            "/f");

        if (code != 0)
        {
            _log.Write(LogLevel.Warn,
                $"tache de sortie non creee (code {code}) : {FirstLine(output)}");
            return false;
        }

        return IsScheduledTaskArmed();
    }

    /// <summary>Vrai si au moins le mecanisme decisif est en place.</summary>
    public bool IsSafeModeExitArmed()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(RunOncePath);
            return key?.GetValue(RunOnceName) is not null;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }

    public bool IsScheduledTaskArmed()
    {
        var (code, output) = Run("schtasks", "/query", "/tn", TaskName);
        return code == 0 && !output.Contains("ERROR", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ecrit le fichier de commandes qui retire l'indicateur de mode sans echec puis
    /// se desinscrit. Il vit dans ProgramData : il doit survivre au deplacement de
    /// l'executable portable.
    /// </summary>
    private static string WriteExitScript()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "ForgeConflict");

        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "sortie-mode-sans-echec.bat");

        var content = string.Join(Environment.NewLine,
            "@echo off",
            "REM Retire l'indicateur de mode sans echec, puis se desinscrit.",
            "REM Ecrit par ForgeConflict AVANT toute entree en mode sans echec.",
            "REM Le retrait de l'indicateur vient EN PREMIER : si la suite echoue,",
            "REM la machine redemarrera quand meme en mode normal.",
            "bcdedit /deletevalue {current} safeboot",
            $"reg delete \"HKLM\\{RunOncePath}\" /v \"{RunOnceName}\" /f",
            $"schtasks /delete /tn \"{TaskName}\" /f",
            "exit /b 0");

        File.WriteAllText(path, content, Encoding.ASCII);
        return path;
    }

    public bool DisarmSafeModeExit()
    {
        var (code, _) = Run("schtasks", "/delete", "/tn", TaskName, "/f");

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(RunOncePath, writable: true);
            key?.DeleteValue(RunOnceName, throwOnMissingValue: false);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            _log.Write(LogLevel.Warn, $"entree RunOnce non retiree : {ex.Message}");
        }

        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "ForgeConflict", "sortie-mode-sans-echec.bat");

            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Write(LogLevel.Warn, $"script de sortie non efface : {ex.Message}");
        }

        return code == 0;
    }

    /// <summary>
    /// Demande un demarrage en mode sans echec. Refuse d'agir si la sortie n'a pas
    /// ete armee : entrer sans garantie de sortie est exactement ce qu'il ne faut
    /// jamais faire.
    /// </summary>
    public bool EnterSafeMode(bool exitArmed)
    {
        if (!exitArmed)
        {
            _log.Write(LogLevel.Error,
                "entree en mode sans echec refusee : la sortie n'a pas ete armee");
            return false;
        }

        var (code, output) = Run("bcdedit", "/set", "{current}", "safeboot", "minimal");

        if (code != 0)
        {
            _log.Write(LogLevel.Error, $"entree en mode sans echec impossible : {FirstLine(output)}");
            return false;
        }

        _log.Write(LogLevel.Warn, "mode sans echec arme pour le prochain demarrage");
        return true;
    }

    public bool ExitSafeMode()
    {
        var (code, output) = Run("bcdedit", "/deletevalue", "{current}", "safeboot");

        // Le code 1 signifie souvent que la valeur n'existait pas : la machine est
        // deja en mode normal, ce qui est le resultat vise.
        if (code != 0 && !output.Contains("introuvable", StringComparison.OrdinalIgnoreCase)
                      && !output.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            _log.Write(LogLevel.Error, $"sortie de mode sans echec impossible : {FirstLine(output)}");
            return false;
        }

        _log.Write(LogLevel.Info, "mode normal retabli pour le prochain demarrage");
        return true;
    }

    /// <summary>Vrai si Windows tourne actuellement en mode sans echec.</summary>
    public static bool IsInSafeMode() =>
        Environment.GetEnvironmentVariable("SAFEBOOT_OPTION") is not null ||
        SystemParametersIsSafeBoot();

    private static bool SystemParametersIsSafeBoot()
    {
        try
        {
            // GetSystemMetrics(SM_CLEANBOOT) : 0 = normal, 1 = minimal, 2 = reseau.
            return NativeSafeBoot.GetSystemMetrics(67) != 0;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
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
        };

        foreach (var argument in arguments) info.ArgumentList.Add(argument);

        using var process = Process.Start(info);
        if (process is null) return (-1, "processus non lance");

        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();

        if (!process.WaitForExit(60_000))
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            return (-1, "delai depasse");
        }

        return (process.ExitCode, output);
    }

    private static string FirstLine(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? string.Empty;
}

internal static class NativeSafeBoot
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    internal static extern int GetSystemMetrics(int index);
}
