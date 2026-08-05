using ForgeConflict.Core.Inventory;
using ForgeConflict.Core.Inventory.Collectors;

namespace ForgeConflict.Tests.Inventory;

public sealed class GpuCollectorTests
{
    [Fact]
    public void Collect_ReturnsAtLeastOneAdapterOnAnyWindowsMachine()
    {
        var gpus = new GpuCollector().Collect(CancellationToken.None);

        Assert.NotEmpty(gpus);
        Assert.All(gpus, g => Assert.False(string.IsNullOrWhiteSpace(g.Name)));
    }

    [Fact]
    public void Collect_ResolvesVendorFromDeviceIdWhenPciDeviceIsPresent()
    {
        var gpus = new GpuCollector().Collect(CancellationToken.None);

        foreach (var gpu in gpus.Where(g => g.DeviceId.StartsWith("PCI", StringComparison.OrdinalIgnoreCase)))
        {
            Assert.NotEqual(GpuVendor.Unknown, gpu.Vendor);
        }
    }

    [Fact]
    public void Collect_HonoursCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // L'annulation doit interrompre proprement, jamais laisser un resultat partiel
        // passer pour un inventaire complet. ThrowsAny et non Throws : le type exact
        // varie selon le point d'interruption (OperationCanceledException ou derivee).
        Assert.ThrowsAny<OperationCanceledException>(() => new GpuCollector().Collect(cts.Token));
    }

    [Fact]
    public void Metadata_IsStableAndUsable()
    {
        var collector = new GpuCollector();

        Assert.Equal("gpu", collector.Source);
        Assert.True(collector.Timeout > TimeSpan.Zero);
    }
}
