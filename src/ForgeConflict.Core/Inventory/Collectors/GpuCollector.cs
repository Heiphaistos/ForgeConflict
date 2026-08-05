using System.Globalization;
using System.Management;
using ForgeConflict.Core.Inventory.Model;

namespace ForgeConflict.Core.Inventory.Collectors;

/// <summary>
/// Inventorie les adaptateurs graphiques. Deux sources sont croisees :
/// Win32_VideoController pour l'etat et la memoire, Win32_PnPSignedDriver pour le
/// paquet de pilote et la signature. Toutes deux sont structurees et independantes
/// de la langue du systeme, contrairement a la sortie texte de pnputil.
/// </summary>
public sealed class GpuCollector : ICollector<IReadOnlyList<GpuInfo>>
{
    public string Source => "gpu";

    public TimeSpan Timeout => TimeSpan.FromSeconds(20);

    public IReadOnlyList<GpuInfo> Collect(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var drivers = ReadSignedDrivers(cancellationToken);
        var result = new List<GpuInfo>();

        using var searcher = new ManagementObjectSearcher(
            "SELECT Name, PNPDeviceID, DriverVersion, DriverDate, AdapterRAM, Status FROM Win32_VideoController");
        using var controllers = searcher.Get();

        foreach (var item in controllers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var controller = item;

            var deviceId = Text(controller["PNPDeviceID"]) ?? string.Empty;
            drivers.TryGetValue(deviceId, out var driver);

            result.Add(new GpuInfo(
                Name: Text(controller["Name"]) ?? "Adaptateur inconnu",
                Vendor: GpuVendorReader.FromDeviceId(deviceId),
                DeviceId: deviceId,
                DriverVersion: driver?.Version ?? Text(controller["DriverVersion"]),
                DriverDate: driver?.Date ?? ParseCimDate(Text(controller["DriverDate"])),
                InfName: driver?.InfName,
                IsSigned: driver?.IsSigned ?? false,
                AdapterRamBytes: ToLong(controller["AdapterRAM"]),
                Status: Text(controller["Status"])));
        }

        return result;
    }

    private static Dictionary<string, DriverRow> ReadSignedDrivers(CancellationToken cancellationToken)
    {
        var map = new Dictionary<string, DriverRow>(StringComparer.OrdinalIgnoreCase);

        using var searcher = new ManagementObjectSearcher(
            "SELECT DeviceID, DriverVersion, DriverDate, InfName, IsSigned FROM Win32_PnPSignedDriver WHERE DeviceClass='DISPLAY'");
        using var rows = searcher.Get();

        foreach (var item in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var row = item;

            var id = Text(row["DeviceID"]);
            if (string.IsNullOrWhiteSpace(id)) continue;

            map[id] = new DriverRow(
                Text(row["DriverVersion"]),
                ParseCimDate(Text(row["DriverDate"])),
                Text(row["InfName"]),
                row["IsSigned"] is bool signed && signed);
        }

        return map;
    }

    private static string? Text(object? value) => value?.ToString();

    private static long ToLong(object? value) =>
        value is null ? 0L : Convert.ToInt64(value, CultureInfo.InvariantCulture);

    /// <summary>
    /// WMI rend les dates au format CIM (yyyyMMddHHmmss.ffffff+UUU) via WQL brut.
    /// ManagementObjectSearcher peut aussi rendre un DateTime deja converti selon
    /// le fournisseur : les deux formes sont acceptees.
    /// </summary>
    private static DateTimeOffset? ParseCimDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        if (DateTimeOffset.TryParse(raw, CultureInfo.CurrentCulture, DateTimeStyles.None, out var parsed))
        {
            return parsed;
        }

        if (raw.Length >= 14 &&
            DateTime.TryParseExact(raw[..14], "yyyyMMddHHmmss", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var cim))
        {
            return new DateTimeOffset(cim, TimeSpan.Zero);
        }

        return null;
    }

    private sealed record DriverRow(string? Version, DateTimeOffset? Date, string? InfName, bool IsSigned);
}
