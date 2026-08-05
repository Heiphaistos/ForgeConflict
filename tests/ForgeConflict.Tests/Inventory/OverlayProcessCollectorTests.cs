using ForgeConflict.Core.Inventory.Collectors;

namespace ForgeConflict.Tests.Inventory;

public sealed class OverlayProcessCollectorTests
{
    [Theory]
    [InlineData("Discord", true)]
    [InlineData("RTSS", true)]
    [InlineData("MSIAfterburner", true)]
    [InlineData("obs64", true)]
    [InlineData("steamwebhelper", true)]
    [InlineData("notepad", false)]
    [InlineData("explorer", false)]
    public void IsKnownOverlay_RecognisesRenderingHooks(string processName, bool expected) =>
        Assert.Equal(expected, OverlayProcessCollector.IsKnownOverlay(processName));

    [Fact]
    public void IsKnownOverlay_IgnoresCaseAndExtension()
    {
        Assert.True(OverlayProcessCollector.IsKnownOverlay("DISCORD.EXE"));
        Assert.True(OverlayProcessCollector.IsKnownOverlay("obs64.exe"));
    }

    [Fact]
    public void IsKnownOverlay_TreatsNullAsNotAnOverlay() =>
        Assert.False(OverlayProcessCollector.IsKnownOverlay(null));

    [Fact]
    public void Collect_NeverThrowsOnProcessesThatVanishOrDenyAccess()
    {
        // Un processus peut disparaitre entre l'enumeration et la lecture de ses
        // proprietes, et certains refusent l'acces meme en administrateur.
        var processes = new OverlayProcessCollector().Collect(CancellationToken.None);

        Assert.All(processes, p => Assert.False(string.IsNullOrWhiteSpace(p.Name)));
    }

    [Fact]
    public void Metadata_IsStableAndUsable()
    {
        var collector = new OverlayProcessCollector();

        Assert.Equal("processus-overlay", collector.Source);
        Assert.True(collector.Timeout > TimeSpan.Zero);
    }
}
