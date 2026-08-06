using System.Diagnostics;
using ForgeConflict.Core.Logging;

namespace ForgeConflict.Core.Safety;

public sealed record RestorePointResult(bool Created, string Message);

/// <summary>
/// Cree un point de restauration systeme avant une modification a risque eleve.
///
/// La creation peut echouer legitimement : la protection du systeme est parfois
/// desactivee, et Windows refuse plus d'un point par periode de 24 heures. Un echec
/// n'empeche pas la correction, mais l'utilisateur doit le savoir avant de decider :
/// le journal transactionnel reste alors le seul filet.
/// </summary>
public sealed class SystemRestorePoint
{
    private readonly ILog _log;

    public SystemRestorePoint(ILog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        _log = log;
    }

    public RestorePointResult Create(string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        try
        {
            var info = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            info.ArgumentList.Add("-NoProfile");
            info.ArgumentList.Add("-NonInteractive");
            info.ArgumentList.Add("-Command");
            info.ArgumentList.Add(
                $"Checkpoint-Computer -Description '{description.Replace("'", "''")}' " +
                "-RestorePointType MODIFY_SETTINGS");

            using var process = Process.Start(info);
            if (process is null)
            {
                return new RestorePointResult(false, "Impossible de lancer la creation du point de restauration.");
            }

            var error = process.StandardError.ReadToEnd();

            if (!process.WaitForExit(120_000))
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                return new RestorePointResult(false, "La creation du point de restauration a depasse le delai imparti.");
            }

            if (process.ExitCode == 0)
            {
                _log.Write(LogLevel.Info, $"point de restauration cree : {description}");
                return new RestorePointResult(true, "Point de restauration cree.");
            }

            // Cause la plus frequente : la protection du systeme est desactivee, ou
            // un point a deja ete cree dans les 24 dernieres heures.
            var reason = string.IsNullOrWhiteSpace(error)
                ? "cause inconnue"
                : error.Split('\n')[0].Trim();

            _log.Write(LogLevel.Warn, $"point de restauration non cree : {reason}");

            return new RestorePointResult(false,
                "Aucun point de restauration n'a pu etre cree (" + reason + "). " +
                "La protection du systeme est peut-etre desactivee, ou un point a deja ete cree " +
                "aujourd'hui. Le journal de ForgeConflict reste utilisable pour annuler la modification.");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            _log.Write(LogLevel.Error, "creation du point de restauration impossible", ex);
            return new RestorePointResult(false, $"Point de restauration impossible : {ex.Message}");
        }
    }
}
