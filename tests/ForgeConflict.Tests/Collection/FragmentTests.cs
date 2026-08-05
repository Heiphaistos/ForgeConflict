using ForgeConflict.Core.Collection;

namespace ForgeConflict.Tests.Collection;

public sealed class FragmentTests
{
    [Fact]
    public void Ok_CarriesValueAndSuccessStatus()
    {
        var fragment = Fragment<int>.Ok("gpu", 42, TimeSpan.FromMilliseconds(12));

        Assert.Equal(FragmentStatus.Ok, fragment.Status);
        Assert.Equal(42, fragment.Value);
        Assert.Null(fragment.Error);
        Assert.True(fragment.IsUsable);
    }

    [Fact]
    public void Failed_CarriesErrorAndNoValue()
    {
        var fragment = Fragment<int>.Failed("gpu", "acces refuse", TimeSpan.Zero);

        Assert.Equal(FragmentStatus.Failed, fragment.Status);
        Assert.Equal(0, fragment.Value);
        Assert.Equal("acces refuse", fragment.Error);
        Assert.False(fragment.IsUsable);
    }

    [Fact]
    public void TimedOut_IsDistinctFromFailed()
    {
        var fragment = Fragment<int>.TimedOut("wmi", TimeSpan.FromSeconds(5));

        Assert.Equal(FragmentStatus.TimedOut, fragment.Status);
        Assert.False(fragment.IsUsable);
        Assert.NotEqual(FragmentStatus.Failed, fragment.Status);
    }
}
