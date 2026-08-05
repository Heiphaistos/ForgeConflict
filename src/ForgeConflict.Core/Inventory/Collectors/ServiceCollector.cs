using System.Management;
using ForgeConflict.Core.Inventory.Model;

namespace ForgeConflict.Core.Inventory.Collectors;

/// <summary>
/// Recense les services lies a un pilote graphique. Les motifs sont assez longs pour
/// eviter les faux positifs : "nv" seul apparait dans des noms sans aucun rapport.
/// </summary>
public sealed class ServiceCollector : ICollector<IReadOnlyList<ServiceInfo>>
{
    private static readonly (string Pattern, GpuVendor Vendor)[] Patterns =
    [
        ("nvdisplay", GpuVendor.Nvidia),
        ("nvcontainer", GpuVendor.Nvidia),
        ("nvagent", GpuVendor.Nvidia),
        ("nvlddmkm", GpuVendor.Nvidia),
        ("nvsvc", GpuVendor.Nvidia),
        ("nvidia", GpuVendor.Nvidia),
        ("amdexternalevents", GpuVendor.Amd),
        ("amdwddm", GpuVendor.Amd),
        ("amdkmdap", GpuVendor.Amd),
        ("amdlog", GpuVendor.Amd),
        ("radeon", GpuVendor.Amd),
        ("igfx", GpuVendor.Intel),
        ("intelgraphics", GpuVendor.Intel),
        ("igcc", GpuVendor.Intel),
        ("inteldisplay", GpuVendor.Intel),
    ];

    public string Source => "services";

    public TimeSpan Timeout => TimeSpan.FromSeconds(20);

    public static GpuVendor ClassifyVendor(string? serviceName)
    {
        if (string.IsNullOrWhiteSpace(serviceName)) return GpuVendor.Unknown;

        // Les espaces sont retires des deux cotes : les noms affiches en contiennent,
        // les noms courts non, et les motifs doivent couvrir les deux formes.
        var normalized = serviceName.Replace(" ", string.Empty).ToLowerInvariant();

        foreach (var (pattern, vendor) in Patterns)
        {
            if (normalized.Contains(pattern, StringComparison.Ordinal))
            {
                return vendor;
            }
        }

        return GpuVendor.Unknown;
    }

    public IReadOnlyList<ServiceInfo> Collect(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var services = new List<ServiceInfo>();

        using var searcher = new ManagementObjectSearcher(
            "SELECT Name, DisplayName, State, StartMode, PathName FROM Win32_Service");
        using var rows = searcher.Get();

        foreach (var item in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var row = item;

            var name = row["Name"]?.ToString();
            var display = row["DisplayName"]?.ToString();

            // Le nom court et le nom affiche sont tous deux examines : certains
            // services constructeurs n'exposent le fournisseur que dans l'un des deux.
            var vendor = ClassifyVendor(name);
            if (vendor == GpuVendor.Unknown) vendor = ClassifyVendor(display);
            if (vendor == GpuVendor.Unknown) continue;

            services.Add(new ServiceInfo(
                Name: name ?? string.Empty,
                DisplayName: display,
                State: row["State"]?.ToString(),
                StartMode: row["StartMode"]?.ToString(),
                PathName: row["PathName"]?.ToString(),
                RelatedVendor: vendor));
        }

        return services;
    }
}
