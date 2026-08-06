using ForgeConflict.Core.Catalog;
using ForgeConflict.Core.Inventory;
using ForgeConflict.Tests.Rules;

namespace ForgeConflict.Tests.Catalog;

public sealed class DriverCatalogTests
{
    [Fact]
    public void Embedded_CoversTheThreeVendors()
    {
        var vendors = DriverCatalog.Embedded().Releases.Select(r => r.Vendor).Distinct().ToList();

        Assert.Contains(GpuVendor.Nvidia, vendors);
        Assert.Contains(GpuVendor.Amd, vendors);
        Assert.Contains(GpuVendor.Intel, vendors);
    }

    [Fact]
    public void Embedded_OffersARecommendedReleasePerVendor()
    {
        var catalog = DriverCatalog.Embedded();

        foreach (var vendor in new[] { GpuVendor.Nvidia, GpuVendor.Amd, GpuVendor.Intel })
        {
            Assert.Contains(catalog.Releases,
                r => r.Vendor == vendor && r.Verdict == Verdict.Recommande);
        }
    }

    [Fact]
    public void RoundTrip_PreservesEveryRelease()
    {
        var catalog = DriverCatalog.Embedded();

        var restored = DriverCatalog.FromJson(catalog.ToJson());

        Assert.NotNull(restored);
        Assert.Equal(catalog.Releases.Count, restored!.Releases.Count);
        Assert.Equal(catalog.UpdatedAt, restored.UpdatedAt);
    }

    [Fact]
    public void FromJson_ReturnsNullOnGarbageRatherThanThrowing()
    {
        // Un catalogue telecharge corrompu ne doit pas priver l'utilisateur du
        // catalogue embarque.
        Assert.Null(DriverCatalog.FromJson("{ pas du json"));
        Assert.Null(DriverCatalog.FromJson(""));
    }

    [Fact]
    public void Covers_MatchesAFamilyRatherThanAnExactName()
    {
        var release = DriverCatalog.Embedded().Releases
            .First(r => r.Vendor == GpuVendor.Nvidia && r.Version == "576.88");

        var laptop = new SnapshotBuilder()
            .WithGpu("NVIDIA GeForce RTX 3070 Laptop GPU", GpuVendor.Nvidia)
            .Build().Gpus.Value!.Single();

        Assert.True(release.Covers(laptop));
    }

    [Fact]
    public void Covers_RejectsAGpuFromAnotherVendor()
    {
        var release = DriverCatalog.Embedded().Releases.First(r => r.Vendor == GpuVendor.Nvidia);

        var intel = new SnapshotBuilder()
            .WithGpu("Intel UHD Graphics", GpuVendor.Intel)
            .Build().Gpus.Value!.Single();

        Assert.False(release.Covers(intel));
    }
}

public sealed class DriverAdvisorTests
{
    private static DriverAdvisor Advisor() => new(DriverCatalog.Embedded());

    [Fact]
    public void Advise_SaysSoWhenTheCardIsUnknown()
    {
        var snapshot = new SnapshotBuilder()
            .WithGpu("Carte exotique 9999", GpuVendor.Nvidia)
            .Build();

        var advice = Assert.Single(Advisor().Advise(snapshot));

        Assert.Null(advice.Recommended);
        Assert.Contains("catalogue", advice.Reasoning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Advise_NeverRecommendsAReleaseWithASeriousKnownIssue()
    {
        var snapshot = new SnapshotBuilder()
            .WithGpu("NVIDIA GeForce RTX 4080", GpuVendor.Nvidia)
            .Build();

        var advice = Assert.Single(Advisor().Advise(snapshot));

        Assert.NotNull(advice.Recommended);
        Assert.False(advice.Recommended!.HasSeriousIssue);
        Assert.NotEqual(Verdict.AEviter, advice.Recommended.Verdict);
    }

    [Fact]
    public void Advise_PrefersAFlawlessReleaseOnAHybridMachine()
    {
        // Sur configuration hybride, la stabilite prime : une version portant meme
        // un probleme notable est ecartee.
        var snapshot = new SnapshotBuilder()
            .WithGpu("Intel UHD Graphics", GpuVendor.Intel)
            .WithGpu("NVIDIA GeForce RTX 3070 Laptop GPU", GpuVendor.Nvidia)
            .Build();

        var advice = Advisor().Advise(snapshot)
            .Single(a => a.AdapterName.Contains("NVIDIA", StringComparison.Ordinal));

        Assert.Empty(advice.Recommended!.KnownIssues);
        Assert.Contains("deux processeurs graphiques", advice.Reasoning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Advise_TightensTheChoiceAfterCrashes()
    {
        var snapshot = new SnapshotBuilder()
            .WithGpu("NVIDIA GeForce RTX 4080", GpuVendor.Nvidia)
            .WithTimeoutRecovery(3)
            .Build();

        var advice = Assert.Single(Advisor().Advise(snapshot));

        Assert.Empty(advice.Recommended!.KnownIssues);
        Assert.Contains("incidents", advice.Reasoning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Advise_PrefersTheStudioBranchForCreation()
    {
        var snapshot = new SnapshotBuilder()
            .WithGpu("NVIDIA GeForce RTX 4080", GpuVendor.Nvidia)
            .Build();

        var advice = Assert.Single(Advisor().Advise(snapshot, UsageProfile.Creation));

        Assert.Equal(DriverBranch.Studio, advice.Recommended!.Branch);
    }

    [Fact]
    public void Advise_IgnoresAdaptersRunningOnTheGenericDriver()
    {
        var snapshot = new SnapshotBuilder()
            .WithGpu("Microsoft Basic Display Adapter", GpuVendor.Microsoft, @"ROOT\BASICDISPLAY")
            .Build();

        Assert.Empty(Advisor().Advise(snapshot));
    }

    [Fact]
    public void Advise_ExplainsItselfInEveryCase()
    {
        var snapshot = new SnapshotBuilder()
            .WithGpu("NVIDIA GeForce RTX 4080", GpuVendor.Nvidia)
            .Build();

        Assert.All(Advisor().Advise(snapshot),
            a => Assert.False(string.IsNullOrWhiteSpace(a.Reasoning)));
    }

    [Fact]
    public void Catalog_ReportsItsOwnAgeSoAdviceCanBeWeighed()
    {
        var old = new DriverCatalog(DateTimeOffset.Now.AddDays(-200), DriverCatalog.Embedded().Releases);

        Assert.True(old.IsStale);
        Assert.False(DriverCatalog.Embedded().IsStale);
    }
}
