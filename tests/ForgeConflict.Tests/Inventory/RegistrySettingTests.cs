using ForgeConflict.Core.Inventory.Model;

namespace ForgeConflict.Tests.Inventory;

public sealed class RegistrySettingTests
{
    [Fact]
    public void Absent_ReportsTheDocumentedDefaultRatherThanZero()
    {
        // TdrDelay absent signifie 2 secondes, pas 0. Lire "absent" comme zero ferait
        // conclure a un TDR desactive, ce qui est faux et dangereux.
        var setting = RegistrySetting.Absent("TdrDelay", effectiveDefault: 2);

        Assert.False(setting.IsPresent);
        Assert.Null(setting.RawValue);
        Assert.Equal(2, setting.EffectiveValue);
        Assert.True(setting.IsDefault);
    }

    [Fact]
    public void Present_WithValueEqualToDefaultIsStillMarkedPresent()
    {
        var setting = RegistrySetting.Present("TdrDelay", value: 2, effectiveDefault: 2);

        Assert.True(setting.IsPresent);
        Assert.Equal(2, setting.RawValue);
        Assert.Equal(2, setting.EffectiveValue);
        Assert.True(setting.IsDefault);
    }

    [Fact]
    public void Present_WithDifferentValueIsNotDefault()
    {
        var setting = RegistrySetting.Present("TdrDelay", value: 8, effectiveDefault: 2);

        Assert.True(setting.IsPresent);
        Assert.Equal(8, setting.EffectiveValue);
        Assert.False(setting.IsDefault);
    }

    [Fact]
    public void Describe_StatesExplicitlyWhetherTheValueWasSet()
    {
        Assert.Contains("non definie", RegistrySetting.Absent("HwSchMode", 2).Describe(), StringComparison.Ordinal);
        Assert.Contains("definie", RegistrySetting.Present("HwSchMode", 1, 2).Describe(), StringComparison.Ordinal);
    }
}
