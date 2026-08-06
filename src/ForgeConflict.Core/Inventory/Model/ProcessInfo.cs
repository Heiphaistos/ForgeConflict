namespace ForgeConflict.Core.Inventory.Model;

public sealed record ProcessInfo(
    int Id,
    string Name,
    string? ExecutablePath,
    string? Company);
