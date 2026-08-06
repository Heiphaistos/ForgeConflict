namespace ForgeConflict.Core.Inventory.Model;

public sealed record ServiceInfo(
    string Name,
    string? DisplayName,
    string? State,
    string? StartMode,
    string? PathName,
    GpuVendor RelatedVendor);
