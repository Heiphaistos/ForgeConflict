using ForgeConflict.Core.Collection;
using ForgeConflict.Core.Inventory;

namespace ForgeConflict.Tests.Inventory;

public sealed class SnapshotReportTests
{
    private static Task<SystemSnapshot> ScanAsync() =>
        new InventoryScanner(new IsolatedRunner(new TestLog())).ScanAsync();

    [Fact]
    public async Task Render_StatesEveryUnusableSourceExplicitly()
    {
        var snapshot = await ScanAsync();

        var text = SnapshotReport.Render(snapshot);

        foreach (var fragment in snapshot.UnusableSources)
        {
            Assert.Contains(fragment.Source, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Render_AlwaysNamesTheMachineAndCaptureTime()
    {
        var snapshot = await ScanAsync();

        var text = SnapshotReport.Render(snapshot);

        Assert.Contains(snapshot.MachineName, text, StringComparison.Ordinal);
        Assert.Contains("Capture", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Render_ListsEveryDetectedAdapter()
    {
        var snapshot = await ScanAsync();

        var text = SnapshotReport.Render(snapshot);

        if (snapshot.Gpus.IsUsable && snapshot.Gpus.Value is { } gpus)
        {
            Assert.All(gpus, g => Assert.Contains(g.Name, text, StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task ToEnvelope_ProducesSerializableCapture()
    {
        var snapshot = await ScanAsync();

        var json = SnapshotSerializer.Serialize(SnapshotReport.ToEnvelope(snapshot, "0.2.0"));
        var restored = SnapshotSerializer.Deserialize(json);

        Assert.Equal(snapshot.MachineName, restored.MachineName);
        Assert.Equal(10, restored.Fragments.Count);
        Assert.Contains(restored.Fragments, f => f.Source == "gpu");
    }
}
