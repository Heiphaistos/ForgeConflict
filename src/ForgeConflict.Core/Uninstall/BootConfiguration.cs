using System.Diagnostics;
using ForgeConflict.Core.Logging;

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
    public bool ArmSafeModeExit()
    {
        // La tache s'execute au demarrage, avec les privileges les plus eleves, et
        // se supprime elle-meme apres avoir agi. Meme si l'application ne se lance
        // jamais, la machine ressort du mode sans echec.
        var command =
            "bcdedit /deletevalue {current} safeboot & " +
            $"schtasks /delete /tn \"{TaskName}\" /f";

        var (code, output) = Run("schtasks",
            "/create", "/tn", TaskName,
            "/tr", $"cmd /c \"{command}\"",
            "/sc", "onstart",
            "/ru", "SYSTEM",
            "/rl", "HIGHEST",
            "/f");

        if (code != 0)
        {
            _log.Write(LogLevel.Error,
                $"sortie de mode sans echec NON armee (code {code}) : {FirstLine(output)}");
            return false;
        }

        _log.Write(LogLevel.Info, "sortie de mode sans echec armee");
        return true;
    }

    public bool DisarmSafeModeExit()
    {
        var (code, _) = Run("schtasks", "/delete", "/tn", TaskName, "/f");
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
