using ForgeConflict.Core.Inventory;

namespace ForgeConflict.Tests.Inventory;

public sealed class GpuVendorTests
{
    [Theory]
    [InlineData(@"PCI\VEN_10DE&DEV_249D&SUBSYS_15441025&REV_A1\4&3216B86&0&0008", GpuVendor.Nvidia)]
    [InlineData(@"PCI\VEN_8086&DEV_9A68&SUBSYS_153B1025&REV_01\3&11583659&0&10", GpuVendor.Intel)]
    [InlineData(@"PCI\VEN_1002&DEV_73FF", GpuVendor.Amd)]
    [InlineData(@"PCI\VEN_1022&DEV_1636", GpuVendor.Amd)]
    [InlineData(@"ROOT\BASICDISPLAY", GpuVendor.Unknown)]
    public void FromDeviceId_ReadsVendorFromPciIdentifier(string deviceId, GpuVendor expected) =>
        Assert.Equal(expected, GpuVendorReader.FromDeviceId(deviceId));

    [Fact]
    public void FromDeviceId_IsCaseInsensitive() =>
        Assert.Equal(GpuVendor.Nvidia, GpuVendorReader.FromDeviceId(@"pci\ven_10de&dev_249d"));

    [Fact]
    public void FromDeviceId_TreatsNullOrEmptyAsUnknown()
    {
        Assert.Equal(GpuVendor.Unknown, GpuVendorReader.FromDeviceId(null));
        Assert.Equal(GpuVendor.Unknown, GpuVendorReader.FromDeviceId(""));
    }

    [Fact]
    public void FromDeviceId_DoesNotGuessFromMarketingName()
    {
        // Le nom commercial n'est jamais une source : seul l'identifiant PCI fait foi.
        Assert.Equal(GpuVendor.Unknown, GpuVendorReader.FromDeviceId("NVIDIA GeForce RTX 3070"));
    }
}
