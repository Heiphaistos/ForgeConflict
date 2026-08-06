using ForgeConflict.Core.Collection;
using ForgeConflict.Core.Inventory;

namespace ForgeConflict.Tests.Inventory;

public sealed class SourceHealthTests
{
    private static Fragment<int> Fragment(FragmentStatus status) =>
        new("source", status, 1, status == FragmentStatus.Failed ? "erreur" : null, TimeSpan.FromMilliseconds(5));

    [Fact]
    public void FromFragment_MapsEachStatusToItsOwnLevel()
    {
        Assert.Equal(HealthLevel.Sain, SourceHealth.FromFragment(Fragment(FragmentStatus.Ok)).Level);
        Assert.Equal(HealthLevel.Critique, SourceHealth.FromFragment(Fragment(FragmentStatus.Failed)).Level);
        Assert.Equal(HealthLevel.Attention, SourceHealth.FromFragment(Fragment(FragmentStatus.TimedOut)).Level);
    }

    [Fact]
    public void FromFragment_ExplainsATimeoutDifferentlyFromAFailure()
    {
        var timedOut = SourceHealth.FromFragment(Fragment(FragmentStatus.TimedOut));
        var failed = SourceHealth.FromFragment(Fragment(FragmentStatus.Failed));

        Assert.Contains("delai", timedOut.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(timedOut.Explanation, failed.Explanation);
    }

    [Fact]
    public void Overall_IsCriticalAsSoonAsOneSourceFailed()
    {
        var levels = new[] { HealthLevel.Sain, HealthLevel.Sain, HealthLevel.Critique };

        Assert.Equal(HealthLevel.Critique, SourceHealth.Overall(levels));
    }

    [Fact]
    public void Overall_IsHealthyOnlyWhenEverySourceAnswered()
    {
        Assert.Equal(HealthLevel.Sain, SourceHealth.Overall([HealthLevel.Sain, HealthLevel.Sain]));
        Assert.Equal(HealthLevel.Attention, SourceHealth.Overall([HealthLevel.Sain, HealthLevel.Attention]));
    }

    [Fact]
    public void Overall_OfNothingIsUnknownRatherThanHealthy()
    {
        // Aucune source evaluee ne signifie pas que tout va bien : le dire serait
        // presenter une absence de mesure comme un bon resultat.
        Assert.Equal(HealthLevel.Inconnu, SourceHealth.Overall([]));
    }
}
