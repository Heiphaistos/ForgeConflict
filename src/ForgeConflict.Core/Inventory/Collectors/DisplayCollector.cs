using System.Globalization;
using System.Management;
using ForgeConflict.Core.Inventory.Model;

namespace ForgeConflict.Core.Inventory.Collectors;

/// <summary>
/// Releve le mode d'affichage courant de chaque adaptateur. En configuration hybride
/// les modes different d'un adaptateur a l'autre, ce qui est normal et ne constitue
/// pas une anomalie en soi.
/// </summary>
public sealed class DisplayCollector : ICollector<IReadOnlyList<DisplayMode>>
{
    public string Source => "ecrans";

    public TimeSpan Timeout => TimeSpan.FromSeconds(20);

    public IReadOnlyList<DisplayMode> Collect(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var modes = new List<DisplayMode>();

        using var searcher = new ManagementObjectSearcher(
            "SELECT Name, CurrentHorizontalResolution, CurrentVerticalResolution, CurrentRefreshRate, VideoModeDescription FROM Win32_VideoController");
        using var rows = searcher.Get();

        foreach (var item in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var row = item;

            modes.Add(new DisplayMode(
                AdapterName: row["Name"]?.ToString() ?? "Adaptateur inconnu",
                HorizontalResolution: ToInt(row["CurrentHorizontalResolution"]),
                VerticalResolution: ToInt(row["CurrentVerticalResolution"]),
                RefreshRateHz: ToInt(row["CurrentRefreshRate"]),
                Description: row["VideoModeDescription"]?.ToString()));
        }

        return modes;
    }

    private static int ToInt(object? value) =>
        value is null ? 0 : Convert.ToInt32(value, CultureInfo.InvariantCulture);
}
