namespace ForgeConflict.Native;

/// <summary>
/// Cle de propriete de peripherique (DEVPROPKEY). Le couple GUID + identifiant est
/// fixe par Windows : ces valeurs sont documentees et ne doivent jamais etre devinees.
/// </summary>
public readonly record struct DeviceProperty(Guid Category, uint PropertyId)
{
    // GUID des proprietes PCI : {3ab22e31-8264-4b4e-9af5-a8d2d8e33e62}
    private static readonly Guid PciCategory = new("3ab22e31-8264-4b4e-9af5-a8d2d8e33e62");

    // GUID des proprietes generales de peripherique : {a45c254e-df1c-4efd-8020-67d146a850e0}
    private static readonly Guid DeviceCategory = new("a45c254e-df1c-4efd-8020-67d146a850e0");

    public static DeviceProperty PciCurrentLinkSpeed => new(PciCategory, 9);
    public static DeviceProperty PciCurrentLinkWidth => new(PciCategory, 10);
    public static DeviceProperty PciMaxLinkSpeed => new(PciCategory, 11);
    public static DeviceProperty PciMaxLinkWidth => new(PciCategory, 12);
    public static DeviceProperty DeviceLocationInfo => new(DeviceCategory, 15);
}
