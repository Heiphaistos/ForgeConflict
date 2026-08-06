using ForgeConflict.Core.Collection;

namespace ForgeConflict.Tests.Collection;

public sealed class SnapshotSerializerTests
{
    private static SnapshotEnvelope Sample() => new(
        SchemaVersion: 1,
        CapturedAt: new DateTimeOffset(2026, 8, 5, 12, 0, 0, TimeSpan.Zero),
        MachineName: "BANC-TEST",
        OsVersion: "10.0.26100",
        AppVersion: "0.1.0",
        Fragments:
        [
            new FragmentRecord("gpu", FragmentStatus.Ok, null, 12.5, """{"count":2}"""),
            new FragmentRecord("wmi", FragmentStatus.TimedOut, null, 5000, null),
            new FragmentRecord("registre", FragmentStatus.Failed, "acces refuse", 3, null),
        ]);

    [Fact]
    public void RoundTrip_PreservesEveryField()
    {
        var json = SnapshotSerializer.Serialize(Sample());
        var restored = SnapshotSerializer.Deserialize(json);

        Assert.Equal("BANC-TEST", restored.MachineName);
        Assert.Equal(3, restored.Fragments.Count);
        Assert.Equal(FragmentStatus.TimedOut, restored.Fragments[1].Status);
        Assert.Equal("acces refuse", restored.Fragments[2].Error);
        Assert.Equal("""{"count":2}""", restored.Fragments[0].PayloadJson);
    }

    [Fact]
    public void Serialize_WritesStatusAsReadableText()
    {
        var json = SnapshotSerializer.Serialize(Sample());

        Assert.Contains("\"TimedOut\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"status\": 2", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Deserialize_RejectsMalformedJsonWithClearError()
    {
        var ex = Record.Exception(() => SnapshotSerializer.Deserialize("{ ceci n'est pas du json"));

        Assert.NotNull(ex);
        Assert.IsType<InvalidDataException>(ex);
    }

    [Fact]
    public void Deserialize_RejectsUnsupportedSchemaVersion()
    {
        var json = SnapshotSerializer.Serialize(Sample() with { SchemaVersion = 99 });

        var ex = Assert.Throws<InvalidDataException>(() => SnapshotSerializer.Deserialize(json));
        Assert.Contains("99", ex.Message, StringComparison.Ordinal);
    }
}
