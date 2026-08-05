using ForgeConflict.Core.Inventory.Collectors;

namespace ForgeConflict.Tests.Inventory;

public sealed class GraphicsSettingsCollectorTests
{
    [Fact]
    public void Collect_NeverReportsAbsentKeysAsZero()
    {
        // Sur une machine par defaut ces cles n'existent pas. Les lire comme zero
        // ferait croire a un TDR desactive et a une planification GPU eteinte.
        var settings = new GraphicsSettingsCollector().Collect(CancellationToken.None);

        foreach (var setting in new[]
                 {
                     settings.TdrLevel, settings.TdrDelay, settings.TdrDdiDelay,
                     settings.HardwareAcceleratedScheduling, settings.MultiPlaneOverlay,
                 })
        {
            if (!setting.IsPresent)
            {
                Assert.NotNull(setting.EffectiveValue);
                Assert.True(setting.IsDefault);
            }
        }
    }

    [Fact]
    public void Collect_AppliesDocumentedWindowsDefaults()
    {
        var settings = new GraphicsSettingsCollector().Collect(CancellationToken.None);

        // Defauts Windows documentes : TdrLevel=3 (recuperation), TdrDelay=2 s, TdrDdiDelay=5 s.
        Assert.Equal(3, settings.TdrLevel.EffectiveDefault);
        Assert.Equal(2, settings.TdrDelay.EffectiveDefault);
        Assert.Equal(5, settings.TdrDdiDelay.EffectiveDefault);
    }

    [Fact]
    public void Collect_NamesEverySettingForDisplay()
    {
        var settings = new GraphicsSettingsCollector().Collect(CancellationToken.None);

        Assert.Equal("TdrDelay", settings.TdrDelay.Name);
        Assert.Equal("HwSchMode", settings.HardwareAcceleratedScheduling.Name);
        Assert.Contains("Tdr", settings.TdrLevel.Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public void Metadata_IsStableAndUsable()
    {
        var collector = new GraphicsSettingsCollector();

        Assert.Equal("reglages-graphiques", collector.Source);
        Assert.True(collector.Timeout > TimeSpan.Zero);
    }
}
