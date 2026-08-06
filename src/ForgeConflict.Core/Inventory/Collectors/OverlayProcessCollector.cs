using System.Diagnostics;
using ForgeConflict.Core.Inventory.Model;

namespace ForgeConflict.Core.Inventory.Collectors;

/// <summary>
/// Repere les processus connus pour s'accrocher au rendu : overlays, injecteurs,
/// enregistreurs. C'est la premiere cause de plantage graphique applicatif.
/// Le collecteur constate leur presence, il ne juge pas.
/// </summary>
public sealed class OverlayProcessCollector : ICollector<IReadOnlyList<ProcessInfo>>
{
    private static readonly string[] KnownOverlays =
    [
        "discord", "rtss", "rivatuner", "msiafterburner", "obs64", "obs32",
        "steamwebhelper", "gameoverlayui", "nvcontainer", "nvidiaapp",
        "medal", "razersynapse", "overwolf", "epicgameslauncher",
        "eadesktop", "ubisoftconnect", "reshade", "specialk", "losslessscaling",
        "xsplit", "streamlabs", "bandicam", "fraps",
    ];

    public string Source => "processus-overlay";

    public TimeSpan Timeout => TimeSpan.FromSeconds(20);

    public static bool IsKnownOverlay(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return false;

        var normalized = processName
            .Replace(".exe", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(" ", string.Empty)
            .ToLowerInvariant();

        return KnownOverlays.Any(o => normalized.Contains(o, StringComparison.Ordinal));
    }

    public IReadOnlyList<ProcessInfo> Collect(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var found = new List<ProcessInfo>();

        foreach (var process in Process.GetProcesses())
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (!IsKnownOverlay(process.ProcessName)) continue;

                string? path = null;
                string? company = null;
                try
                {
                    // Un processus protege refuse l'acces a son module principal, meme
                    // en administrateur. On garde alors le nom seul plutot que rien.
                    path = process.MainModule?.FileName;
                    company = process.MainModule?.FileVersionInfo.CompanyName;
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                }

                found.Add(new ProcessInfo(process.Id, process.ProcessName, path, company));
            }
            catch (InvalidOperationException)
            {
                // Le processus s'est termine entre l'enumeration et la lecture.
            }
            finally
            {
                process.Dispose();
            }
        }

        return found;
    }
}
