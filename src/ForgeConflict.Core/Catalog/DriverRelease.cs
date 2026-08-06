using ForgeConflict.Core.Inventory;
using ForgeConflict.Core.Inventory.Model;

namespace ForgeConflict.Core.Catalog;

public enum DriverBranch
{
    GameReady,
    Studio,
    Whql,
    Pro,
    Beta,
}

public enum Verdict
{
    /// <summary>Version conseillee pour cette configuration.</summary>
    Recommande,

    /// <summary>Utilisable sans reserve particuliere.</summary>
    Stable,

    /// <summary>Des problemes connus la rendent deconseillee.</summary>
    AEviter,
}

/// <summary>Probleme connu affectant une version de pilote.</summary>
public sealed record KnownIssue(string Description, string? AffectedConfigurations, Severity Impact);

public enum Severity
{
    Mineur,
    Notable,
    Grave,
}

/// <summary>
/// Une version de pilote publiee. Les familles de GPU sont listees explicitement :
/// une version ne concerne jamais tout le catalogue d'un fournisseur.
/// </summary>
public sealed record DriverRelease(
    GpuVendor Vendor,
    string Version,
    DriverBranch Branch,
    DateTimeOffset ReleaseDate,
    bool Whql,
    IReadOnlyList<string> GpuFamilies,
    IReadOnlyList<KnownIssue> KnownIssues,
    Verdict Verdict,
    string? DownloadUrl)
{
    /// <summary>
    /// Vrai si cette version couvre le GPU indique. La comparaison porte sur des
    /// motifs de famille, pas sur le nom complet : « RTX 30 » couvre aussi bien une
    /// 3070 de bureau qu'une 3070 Laptop.
    /// </summary>
    public bool Covers(GpuInfo gpu)
    {
        ArgumentNullException.ThrowIfNull(gpu);

        if (gpu.Vendor != Vendor) return false;

        return GpuFamilies.Any(family =>
            gpu.Name.Contains(family, StringComparison.OrdinalIgnoreCase));
    }

    public bool HasSeriousIssue => KnownIssues.Any(i => i.Impact == Severity.Grave);
}
