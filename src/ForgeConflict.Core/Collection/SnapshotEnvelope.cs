namespace ForgeConflict.Core.Collection;

public sealed record SnapshotEnvelope(
    int SchemaVersion,
    DateTimeOffset CapturedAt,
    string MachineName,
    string OsVersion,
    string AppVersion,
    IReadOnlyList<FragmentRecord> Fragments)
{
    public const int CurrentSchemaVersion = 1;
}
