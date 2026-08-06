namespace ForgeConflict.Core.Inventory.Model;

public sealed record DisplayMode(
    string AdapterName,
    int HorizontalResolution,
    int VerticalResolution,
    int RefreshRateHz,
    string? Description)
{
    public bool IsActive => HorizontalResolution > 0 && VerticalResolution > 0;
}
