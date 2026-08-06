using System.Runtime.InteropServices;
using System.Text;

namespace ForgeConflict.Native;

/// <summary>
/// Lit les proprietes de peripherique par cfgmgr32. Aucune API managee n'expose la
/// largeur ni la vitesse du lien PCIe, d'ou le recours a l'interoperabilite native.
/// Tous les appels sont en LECTURE : ce type ne modifie jamais l'etat du systeme.
/// </summary>
public sealed class CfgMgrDevicePropertyReader : IDevicePropertyReader
{
    private const int CrSuccess = 0;
    private const int DevpropTypeUInt32 = 0x00000007;
    private const int DevpropTypeString = 0x00000012;

    public bool TryReadUInt32(string? instanceId, DeviceProperty property, out uint value)
    {
        value = 0;

        var buffer = ReadProperty(instanceId, property, out var type);
        if (buffer is null || type != DevpropTypeUInt32 || buffer.Length < sizeof(uint))
        {
            return false;
        }

        value = BitConverter.ToUInt32(buffer, 0);
        return true;
    }

    public bool TryReadString(string? instanceId, DeviceProperty property, out string? value)
    {
        value = null;

        var buffer = ReadProperty(instanceId, property, out var type);
        if (buffer is null || type != DevpropTypeString)
        {
            return false;
        }

        // Chaine UTF-16 terminee par un caractere nul, qu'il faut retirer.
        value = Encoding.Unicode.GetString(buffer).TrimEnd('\0');
        return !string.IsNullOrWhiteSpace(value);
    }

    private static byte[]? ReadProperty(string? instanceId, DeviceProperty property, out int propertyType)
    {
        propertyType = 0;
        if (string.IsNullOrWhiteSpace(instanceId)) return null;

        if (CM_Locate_DevNodeW(out var devInst, instanceId, 0) != CrSuccess)
        {
            return null;
        }

        var key = new DevPropKey { Category = property.Category, PropertyId = property.PropertyId };

        // Premier appel a taille nulle : Windows renseigne la taille necessaire.
        uint size = 0;
        CM_Get_DevNode_PropertyW(devInst, ref key, out propertyType, null, ref size, 0);
        if (size == 0) return null;

        var buffer = new byte[size];
        return CM_Get_DevNode_PropertyW(devInst, ref key, out propertyType, buffer, ref size, 0) == CrSuccess
            ? buffer
            : null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DevPropKey
    {
        public Guid Category;
        public uint PropertyId;
    }

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int CM_Locate_DevNodeW(out uint pdnDevInst, string pDeviceID, uint ulFlags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int CM_Get_DevNode_PropertyW(
        uint dnDevInst,
        ref DevPropKey propertyKey,
        out int propertyType,
        byte[]? propertyBuffer,
        ref uint propertyBufferSize,
        uint ulFlags);
}
