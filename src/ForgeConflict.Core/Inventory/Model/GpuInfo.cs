namespace ForgeConflict.Core.Inventory.Model;

public sealed record GpuInfo(
    string Name,
    GpuVendor Vendor,
    string DeviceId,
    string? DriverVersion,
    DateTimeOffset? DriverDate,
    string? InfName,
    bool IsSigned,
    long AdapterRamBytes,
    string? Status)
{
    /// <summary>Un pilote d'affichage generique signale un pilote constructeur absent ou casse.</summary>
    public bool IsGenericMicrosoftDriver =>
        Vendor == GpuVendor.Microsoft ||
        (Name?.Contains("Basic Display", StringComparison.OrdinalIgnoreCase) ?? false) ||
        (Name?.Contains("Hyper-V Video", StringComparison.OrdinalIgnoreCase) ?? false);
}
