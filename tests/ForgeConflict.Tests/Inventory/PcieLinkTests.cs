using ForgeConflict.Core.Inventory.Model;

namespace ForgeConflict.Tests.Inventory;

public sealed class PcieLinkTests
{
    [Fact]
    public void IsDegraded_IsTrueWhenTheLinkRunsBelowItsMaximumWidth()
    {
        var link = new PcieLink(CurrentWidth: 8, MaxWidth: 16, CurrentSpeedGen: 4, MaxSpeedGen: 4);

        Assert.True(link.IsWidthDegraded);
        Assert.False(link.IsSpeedDegraded);
        Assert.True(link.IsDegraded);
    }

    [Fact]
    public void IsDegraded_IsFalseWhenTheLinkRunsAtItsMaximum()
    {
        var link = new PcieLink(16, 16, 4, 4);

        Assert.False(link.IsDegraded);
    }

    [Fact]
    public void IsDegraded_DetectsSpeedDropIndependentlyOfWidth()
    {
        var link = new PcieLink(16, 16, CurrentSpeedGen: 1, MaxSpeedGen: 4);

        Assert.True(link.IsSpeedDegraded);
        Assert.False(link.IsWidthDegraded);
    }

    [Fact]
    public void Describe_ReadsAsPlainPcieNotation()
    {
        var link = new PcieLink(8, 16, 4, 4);

        var text = link.Describe();

        Assert.Contains("x8", text, StringComparison.Ordinal);
        Assert.Contains("x16", text, StringComparison.Ordinal);
        Assert.Contains("Gen4", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_ReportsNoDegradationWhenNothingCouldBeRead()
    {
        // Un GPU integre et une machine virtuelle n'exposent aucune propriete PCIe :
        // l'absence d'information ne doit jamais passer pour une degradation.
        Assert.False(PcieLink.Unknown.IsDegraded);
        Assert.False(PcieLink.Unknown.IsKnown);
        Assert.Equal("lien PCIe inconnu", PcieLink.Unknown.Describe());
    }
}
