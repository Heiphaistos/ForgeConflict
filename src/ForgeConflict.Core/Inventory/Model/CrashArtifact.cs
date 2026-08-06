namespace ForgeConflict.Core.Inventory.Model;

public enum CrashArtifactKind
{
    LiveKernelReport,
    Minidump,
}

public sealed record CrashArtifact(
    CrashArtifactKind Kind,
    string Path,
    string FileName,
    long SizeBytes,
    DateTimeOffset CreatedAt,
    string? Category);
