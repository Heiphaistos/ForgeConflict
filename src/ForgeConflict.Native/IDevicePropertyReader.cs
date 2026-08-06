namespace ForgeConflict.Native;

/// <summary>
/// Lit une propriete de peripherique. Toutes les methodes rendent faux plutot que de
/// lever : une propriete absente est un cas courant, pas une erreur.
/// </summary>
public interface IDevicePropertyReader
{
    bool TryReadUInt32(string? instanceId, DeviceProperty property, out uint value);

    bool TryReadString(string? instanceId, DeviceProperty property, out string? value);
}
