using ForgeConflict.Core.Collection;
using ForgeConflict.Core.Inventory.Model;

namespace ForgeConflict.Core.Inventory;

/// <summary>
/// Etat graphique de la machine a un instant donne. Chaque source est portee par un
/// <see cref="Fragment{T}"/> : une source defaillante reste visible en tant que telle,
/// pour qu'un diagnostic incomplet ne puisse jamais passer pour un diagnostic complet.
/// </summary>
public sealed record SystemSnapshot(
    DateTimeOffset CapturedAt,
    string MachineName,
    string OsVersion,
    Fragment<IReadOnlyList<GpuInfo>> Gpus,
    Fragment<IReadOnlyList<DriverPackage>> DriverPackages,
    Fragment<IReadOnlyList<ServiceInfo>> Services,
    Fragment<GraphicsSettings> GraphicsSettings,
    Fragment<IReadOnlyList<ProcessInfo>> OverlayProcesses)
{
    public IReadOnlyList<IFragment> AllFragments =>
    [
        Gpus, DriverPackages, Services, GraphicsSettings, OverlayProcesses,
    ];

    /// <summary>Sources dont le resultat est inexploitable, a signaler explicitement.</summary>
    public IReadOnlyList<IFragment> UnusableSources =>
        AllFragments.Where(f => !f.IsUsable).ToList();

    public bool IsComplete => UnusableSources.Count == 0;
}
