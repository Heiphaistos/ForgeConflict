namespace ForgeConflict.Core.Inventory.Model;

public sealed record PowerProfile(
    string? ActiveSchemeGuid,
    string? ActiveSchemeName,
    RegistrySetting PciExpressLinkStateOnAc,
    RegistrySetting PciExpressLinkStateOnBattery);
