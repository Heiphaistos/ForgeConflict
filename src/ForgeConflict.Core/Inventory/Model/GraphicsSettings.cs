namespace ForgeConflict.Core.Inventory.Model;

/// <summary>
/// Reglages graphiques globaux lus sous HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers.
/// Chaque champ porte sa propre distinction absent / defini : voir <see cref="RegistrySetting"/>.
/// </summary>
public sealed record GraphicsSettings(
    RegistrySetting TdrLevel,
    RegistrySetting TdrDelay,
    RegistrySetting TdrDdiDelay,
    RegistrySetting HardwareAcceleratedScheduling,
    RegistrySetting MultiPlaneOverlay);
