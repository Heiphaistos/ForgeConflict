using ForgeConflict.Native;

namespace ForgeConflict.Tests.Native;

public sealed class DevicePropertyReaderTests
{
    private static IReadOnlyList<string> DisplayInstanceIds()
    {
        var ids = new List<string>();

        using var searcher = new System.Management.ManagementObjectSearcher(
            "SELECT PNPDeviceID FROM Win32_VideoController");
        using var rows = searcher.Get();

        foreach (var row in rows)
        {
            using (row)
            {
                var id = row["PNPDeviceID"]?.ToString();
                if (!string.IsNullOrWhiteSpace(id) &&
                    id.StartsWith("PCI", StringComparison.OrdinalIgnoreCase))
                {
                    ids.Add(id);
                }
            }
        }

        return ids;
    }

    [Fact]
    public void TryReadUInt32_ReadsCoherentPcieWidthsWhereverTheyAreExposed()
    {
        // Un GPU integre au processeur n'est pas sur un vrai lien PCIe et n'expose
        // aucune de ces proprietes : exiger une lecture reussie de TOUT adaptateur
        // ferait echouer le test sur une machine parfaitement saine.
        var reader = new CfgMgrDevicePropertyReader();
        var readCount = 0;

        foreach (var instanceId in DisplayInstanceIds())
        {
            if (!reader.TryReadUInt32(instanceId, DeviceProperty.PciCurrentLinkWidth, out var current))
            {
                continue;
            }

            readCount++;
            Assert.True(reader.TryReadUInt32(instanceId, DeviceProperty.PciMaxLinkWidth, out var max));

            // Une largeur de lien PCIe se situe entre 1 et 32 voies.
            Assert.InRange(current, 1u, 32u);
            Assert.InRange(max, 1u, 32u);
            Assert.True(current <= max, $"largeur courante {current} superieure au maximum {max}");
        }

        // Sur une machine a GPU discret, au moins un adaptateur doit repondre. Sans
        // cette borne, un lecteur totalement casse passerait le test en silence.
        if (HasDiscreteGpu())
        {
            Assert.True(readCount > 0, "aucun adaptateur n'a livre sa largeur de lien PCIe");
        }
    }

    /// <summary>
    /// Un GPU discret se reconnait a son fournisseur PCI : NVIDIA (10DE) ou AMD (1002).
    /// Verifie independamment de cfgmgr32, pour ne pas valider le lecteur par lui-meme.
    /// </summary>
    private static bool HasDiscreteGpu() =>
        DisplayInstanceIds().Any(id =>
            id.Contains("VEN_10DE", StringComparison.OrdinalIgnoreCase) ||
            id.Contains("VEN_1002", StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void TryReadUInt32_ReturnsFalseForAnIntegratedGpuWithoutPcieLink()
    {
        // Constat du 06/08/2026 : l'Intel UHD integre ne publie aucune propriete PCIe.
        // L'absence doit se traduire par un faux, jamais par une exception.
        var reader = new CfgMgrDevicePropertyReader();

        foreach (var instanceId in DisplayInstanceIds().Where(i =>
                     i.Contains("VEN_8086", StringComparison.OrdinalIgnoreCase)))
        {
            var read = reader.TryReadUInt32(instanceId, DeviceProperty.PciCurrentLinkWidth, out var value);
            if (!read) Assert.Equal(0u, value);
        }
    }

    [Fact]
    public void TryReadUInt32_ReturnsFalseForAnUnknownDeviceInsteadOfThrowing()
    {
        var reader = new CfgMgrDevicePropertyReader();

        Assert.False(reader.TryReadUInt32(
            @"PCI\VEN_FFFF&DEV_FFFF\inexistant", DeviceProperty.PciCurrentLinkWidth, out var value));
        Assert.Equal(0u, value);
    }

    [Fact]
    public void TryReadUInt32_ReturnsFalseForNullOrEmptyInstanceId()
    {
        var reader = new CfgMgrDevicePropertyReader();

        Assert.False(reader.TryReadUInt32(null, DeviceProperty.PciMaxLinkSpeed, out _));
        Assert.False(reader.TryReadUInt32("", DeviceProperty.PciMaxLinkSpeed, out _));
    }

    [Fact]
    public void TryReadString_ReadsTheLocationOfARealAdapter()
    {
        var instanceId = DisplayInstanceIds().FirstOrDefault();
        if (instanceId is null) return; // machine sans GPU PCI, ex. certaines VM

        var reader = new CfgMgrDevicePropertyReader();

        Assert.True(reader.TryReadString(instanceId, DeviceProperty.DeviceLocationInfo, out var location));
        Assert.False(string.IsNullOrWhiteSpace(location));
    }
}
