using ForgeConflict.Core.Inventory.Model;

namespace ForgeConflict.Core.Inventory.Collectors;

/// <summary>
/// Recense les traces de plantage noyau laissees par un pilote graphique.
/// Une machine saine n'en a aucune : l'absence est un resultat normal.
/// </summary>
public sealed class CrashArtifactCollector : ICollector<IReadOnlyList<CrashArtifact>>
{
    private static readonly string[] DumpExtensions = [".dmp", ".mdmp"];

    public string Source => "traces-plantage";

    public TimeSpan Timeout => TimeSpan.FromSeconds(20);

    public IReadOnlyList<CrashArtifact> Collect(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        var artifacts = new List<CrashArtifact>();
        artifacts.AddRange(Scan(Path.Combine(windows, "LiveKernelReports"), CrashArtifactKind.LiveKernelReport));
        artifacts.AddRange(Scan(Path.Combine(windows, "Minidump"), CrashArtifactKind.Minidump));

        return artifacts;
    }

    /// <summary>
    /// Parcourt un dossier de dumps. La categorie est deduite du dossier parent :
    /// LiveKernelReports range ses traces par type, ex. WATCHDOG ou DXGKRNL.
    /// </summary>
    public static IReadOnlyList<CrashArtifact> Scan(string directory, CrashArtifactKind kind)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return [];

        var artifacts = new List<CrashArtifact>();

        try
        {
            foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                if (!DumpExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                FileInfo info;
                try
                {
                    info = new FileInfo(path);
                    if (!info.Exists) continue;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                var parent = Path.GetDirectoryName(path);
                var category = string.Equals(parent, directory, StringComparison.OrdinalIgnoreCase)
                    ? null
                    : Path.GetFileName(parent);

                artifacts.Add(new CrashArtifact(
                    Kind: kind,
                    Path: path,
                    FileName: info.Name,
                    SizeBytes: info.Length,
                    CreatedAt: new DateTimeOffset(info.CreationTimeUtc, TimeSpan.Zero),
                    Category: category));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Dossier protege ou verrouille : on rend ce qui a pu etre lu.
        }

        return artifacts;
    }
}
