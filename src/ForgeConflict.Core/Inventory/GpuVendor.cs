using System.Text.RegularExpressions;

namespace ForgeConflict.Core.Inventory;

public enum GpuVendor
{
    Unknown,
    Nvidia,
    Amd,
    Intel,
    Microsoft,
}

/// <summary>
/// Deduit le fournisseur du seul identifiant PCI. Le nom commercial n'est jamais
/// utilise : il est libre, traduit et parfois trompeur sur les portables hybrides.
/// </summary>
public static partial class GpuVendorReader
{
    public static GpuVendor FromDeviceId(string? deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) return GpuVendor.Unknown;

        var match = VendorPattern().Match(deviceId);
        if (!match.Success) return GpuVendor.Unknown;

        return match.Groups[1].Value.ToUpperInvariant() switch
        {
            "10DE" => GpuVendor.Nvidia,
            "1002" or "1022" => GpuVendor.Amd,
            "8086" => GpuVendor.Intel,
            "1414" => GpuVendor.Microsoft,
            _ => GpuVendor.Unknown,
        };
    }

    [GeneratedRegex(@"VEN_([0-9A-Fa-f]{4})", RegexOptions.IgnoreCase)]
    private static partial Regex VendorPattern();
}
