using ForgeConflict.Core.Inventory.Model;
using Microsoft.Win32;

namespace ForgeConflict.Core.Inventory.Collectors;

/// <summary>
/// Lit les reglages graphiques globaux. Ces cles sont le plus souvent ABSENTES sur une
/// machine saine : Windows applique alors ses valeurs par defaut. Chaque reglage porte
/// donc son defaut documente, pour qu'une valeur manquante ne soit jamais confondue
/// avec un zero.
/// </summary>
public sealed class GraphicsSettingsCollector : ICollector<GraphicsSettings>
{
    private const string KeyPath = @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers";

    public string Source => "reglages-graphiques";

    public TimeSpan Timeout => TimeSpan.FromSeconds(10);

    public GraphicsSettings Collect(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var key = Registry.LocalMachine.OpenSubKey(KeyPath, writable: false);

        return new GraphicsSettings(
            // Recuperation activee par defaut.
            TdrLevel: Read(key, "TdrLevel", 3),
            // Deux secondes avant de considerer le GPU bloque.
            TdrDelay: Read(key, "TdrDelay", 2),
            // Cinq secondes pour un appel DDI.
            TdrDdiDelay: Read(key, "TdrDdiDelay", 5),
            // Planification GPU acceleree : 2 = disponible mais eteinte.
            HardwareAcceleratedScheduling: Read(key, "HwSchMode", 2),
            // Multi-Plane Overlay : actif tant qu'il n'est pas explicitement desactive.
            MultiPlaneOverlay: Read(key, "OverlayTestMode", 0));
    }

    private static RegistrySetting Read(RegistryKey? key, string name, object effectiveDefault)
    {
        var value = key?.GetValue(name);
        return value is null
            ? RegistrySetting.Absent(name, effectiveDefault)
            : RegistrySetting.Present(name, value, effectiveDefault);
    }
}
