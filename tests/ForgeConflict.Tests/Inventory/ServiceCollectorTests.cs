using ForgeConflict.Core.Inventory;
using ForgeConflict.Core.Inventory.Collectors;

namespace ForgeConflict.Tests.Inventory;

public sealed class ServiceCollectorTests
{
    [Theory]
    [InlineData("NVDisplay.ContainerLocalSystem", GpuVendor.Nvidia)]
    [InlineData("nvagent", GpuVendor.Nvidia)]
    [InlineData("AMD External Events Utility", GpuVendor.Amd)]
    [InlineData("amdwddmg", GpuVendor.Amd)]
    [InlineData("igfxCUIService2.0.0.0", GpuVendor.Intel)]
    [InlineData("IntelGraphicsSoftwareService", GpuVendor.Intel)]
    [InlineData("Spooler", GpuVendor.Unknown)]
    public void ClassifyVendor_MapsKnownGraphicsServices(string name, GpuVendor expected) =>
        Assert.Equal(expected, ServiceCollector.ClassifyVendor(name));

    [Fact]
    public void ClassifyVendor_DoesNotMatchOnAmbiguousSubstrings()
    {
        // "nv" seul est trop court : il apparait dans des noms sans rapport.
        Assert.Equal(GpuVendor.Unknown, ServiceCollector.ClassifyVendor("EnvironmentService"));
        Assert.Equal(GpuVendor.Unknown, ServiceCollector.ClassifyVendor("WinDefend"));
    }

    [Fact]
    public void Collect_ReturnsOnlyGraphicsRelatedServices()
    {
        var services = new ServiceCollector().Collect(CancellationToken.None);

        Assert.All(services, s => Assert.NotEqual(GpuVendor.Unknown, s.RelatedVendor));
        Assert.All(services, s => Assert.False(string.IsNullOrWhiteSpace(s.Name)));
    }

    [Fact]
    public void Metadata_IsStableAndUsable()
    {
        var collector = new ServiceCollector();

        Assert.Equal("services", collector.Source);
        Assert.True(collector.Timeout > TimeSpan.Zero);
    }
}
