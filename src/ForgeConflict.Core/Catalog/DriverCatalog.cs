using System.Text.Json;
using System.Text.Json.Serialization;
using ForgeConflict.Core.Inventory;

namespace ForgeConflict.Core.Catalog;

/// <summary>
/// Base des versions de pilote connues. Elle est embarquee dans l'executable pour
/// que l'application fonctionne entierement hors-ligne, et peut etre rafraichie
/// depuis un fichier telecharge sur demande explicite de l'utilisateur.
///
/// Les donnees ci-dessous refletent l'etat connu au 6 aout 2026. Un catalogue
/// vieillit : la date de derniere mise a jour est affichee a l'utilisateur pour
/// qu'il sache ce que vaut le conseil.
/// </summary>
public sealed class DriverCatalog
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public DriverCatalog(DateTimeOffset updatedAt, IReadOnlyList<DriverRelease> releases)
    {
        ArgumentNullException.ThrowIfNull(releases);

        UpdatedAt = updatedAt;
        Releases = releases;
    }

    public DateTimeOffset UpdatedAt { get; }

    public IReadOnlyList<DriverRelease> Releases { get; }

    /// <summary>Age du catalogue : au-dela de quelques mois, le conseil perd de sa valeur.</summary>
    public int AgeInDays => Math.Max(0, (int)(DateTimeOffset.Now - UpdatedAt).TotalDays);

    public bool IsStale => AgeInDays > 120;

    public static DriverCatalog Embedded() => new(
        new DateTimeOffset(2026, 8, 6, 0, 0, 0, TimeSpan.Zero),
        [
            // --- NVIDIA ---
            new DriverRelease(
                GpuVendor.Nvidia, "581.74", DriverBranch.GameReady,
                new DateTimeOffset(2026, 7, 22, 0, 0, 0, TimeSpan.Zero), Whql: true,
                ["RTX 50", "RTX 40", "RTX 30", "RTX 20", "GTX 16"],
                [
                    new KnownIssue(
                        "Sur portables Optimus, des scintillements sont rapportes au retour de veille.",
                        "Portables a double GPU Intel + NVIDIA", Severity.Notable),
                ],
                Verdict.Stable, "https://www.nvidia.com/fr-fr/geforce/drivers/"),

            new DriverRelease(
                GpuVendor.Nvidia, "576.88", DriverBranch.GameReady,
                new DateTimeOffset(2026, 5, 14, 0, 0, 0, TimeSpan.Zero), Whql: true,
                ["RTX 50", "RTX 40", "RTX 30", "RTX 20", "GTX 16"],
                [],
                Verdict.Recommande, "https://www.nvidia.com/fr-fr/geforce/drivers/"),

            new DriverRelease(
                GpuVendor.Nvidia, "572.16", DriverBranch.Studio,
                new DateTimeOffset(2026, 4, 2, 0, 0, 0, TimeSpan.Zero), Whql: true,
                ["RTX 50", "RTX 40", "RTX 30"],
                [],
                Verdict.Stable, "https://www.nvidia.com/fr-fr/geforce/drivers/"),

            new DriverRelease(
                GpuVendor.Nvidia, "566.03", DriverBranch.GameReady,
                new DateTimeOffset(2026, 2, 11, 0, 0, 0, TimeSpan.Zero), Whql: true,
                ["RTX 40", "RTX 30", "RTX 20"],
                [
                    new KnownIssue(
                        "Reinitialisations du pilote signalees sous charge prolongee.",
                        null, Severity.Grave),
                ],
                Verdict.AEviter, null),

            // --- AMD ---
            new DriverRelease(
                GpuVendor.Amd, "26.7.1", DriverBranch.Whql,
                new DateTimeOffset(2026, 7, 15, 0, 0, 0, TimeSpan.Zero), Whql: true,
                ["RX 9000", "RX 7000", "RX 6000", "Radeon 800M", "Radeon 700M"],
                [],
                Verdict.Recommande, "https://www.amd.com/fr/support"),

            new DriverRelease(
                GpuVendor.Amd, "26.3.2", DriverBranch.Whql,
                new DateTimeOffset(2026, 3, 18, 0, 0, 0, TimeSpan.Zero), Whql: true,
                ["RX 9000", "RX 7000", "RX 6000"],
                [
                    new KnownIssue(
                        "Consommation au repos anormalement elevee sur configurations multi-ecrans.",
                        "Deux ecrans ou plus a frequences differentes", Severity.Notable),
                ],
                Verdict.Stable, "https://www.amd.com/fr/support"),

            // --- Intel ---
            new DriverRelease(
                GpuVendor.Intel, "32.0.101.7088", DriverBranch.Whql,
                new DateTimeOffset(2026, 6, 17, 0, 0, 0, TimeSpan.Zero), Whql: true,
                ["Arc", "Iris Xe", "UHD Graphics"],
                [],
                Verdict.Recommande, "https://www.intel.fr/content/www/fr/fr/download-center/home.html"),

            new DriverRelease(
                GpuVendor.Intel, "31.0.101.5534", DriverBranch.Whql,
                new DateTimeOffset(2025, 11, 20, 0, 0, 0, TimeSpan.Zero), Whql: true,
                ["Iris Xe", "UHD Graphics"],
                [],
                Verdict.Stable, "https://www.intel.fr/content/www/fr/fr/download-center/home.html"),
        ]);

    public string ToJson() => JsonSerializer.Serialize(
        new Snapshot(UpdatedAt, Releases), Options);

    /// <summary>
    /// Relit un catalogue telecharge. Un fichier illisible rend null plutot que de
    /// lever : le catalogue embarque reste alors en place, et le conseil demeure
    /// disponible.
    /// </summary>
    public static DriverCatalog? FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            var snapshot = JsonSerializer.Deserialize<Snapshot>(json, Options);
            return snapshot is null || snapshot.Releases.Count == 0
                ? null
                : new DriverCatalog(snapshot.UpdatedAt, snapshot.Releases);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record Snapshot(DateTimeOffset UpdatedAt, IReadOnlyList<DriverRelease> Releases);
}
