using System.Management;
using ForgeConflict.Core.Inventory.Model;
using ForgeConflict.Native;

namespace ForgeConflict.Core.Inventory.Collectors;

/// <summary>
/// Releve l'etat du lien PCIe de chaque adaptateur graphique. Les proprietes sont
/// lues par cfgmgr32 : aucune API managee ne les expose.
/// </summary>
public sealed class PcieCollector : ICollector<IReadOnlyList<KeyValuePair<string, PcieLink>>>
{
    private readonly IDevicePropertyReader _reader;

    public PcieCollector() : this(new CfgMgrDevicePropertyReader())
    {
    }

    public PcieCollector(IDevicePropertyReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        _reader = reader;
    }

    public string Source => "materiel-pcie";

    public TimeSpan Timeout => TimeSpan.FromSeconds(20);

    /// <summary>
    /// Assemble l'etat du lien. Si aucune propriete n'est lisible, rend
    /// <see cref="PcieLink.Unknown"/> : un GPU integre ou une machine virtuelle
    /// n'expose rien, et cette absence n'est jamais une degradation.
    /// </summary>
    public static PcieLink ReadLink(IDevicePropertyReader reader, string instanceId)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var hasWidth = reader.TryReadUInt32(instanceId, DeviceProperty.PciCurrentLinkWidth, out var currentWidth);
        reader.TryReadUInt32(instanceId, DeviceProperty.PciMaxLinkWidth, out var maxWidth);
        reader.TryReadUInt32(instanceId, DeviceProperty.PciCurrentLinkSpeed, out var currentSpeed);
        reader.TryReadUInt32(instanceId, DeviceProperty.PciMaxLinkSpeed, out var maxSpeed);

        return hasWidth
            ? new PcieLink(currentWidth, maxWidth, currentSpeed, maxSpeed)
            : PcieLink.Unknown;
    }

    public IReadOnlyList<KeyValuePair<string, PcieLink>> Collect(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var links = new List<KeyValuePair<string, PcieLink>>();

        using var searcher = new ManagementObjectSearcher(
            "SELECT Name, PNPDeviceID FROM Win32_VideoController");
        using var rows = searcher.Get();

        foreach (var item in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var row = item;

            var name = row["Name"]?.ToString() ?? "Adaptateur inconnu";
            var instanceId = row["PNPDeviceID"]?.ToString();

            links.Add(new KeyValuePair<string, PcieLink>(name, ReadLink(_reader, instanceId ?? string.Empty)));
        }

        return links;
    }
}
