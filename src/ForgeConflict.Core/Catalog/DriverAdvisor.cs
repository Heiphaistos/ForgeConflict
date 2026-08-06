using ForgeConflict.Core.Inventory;
using ForgeConflict.Core.Inventory.Model;

namespace ForgeConflict.Core.Catalog;

public enum UsageProfile
{
    Jeu,
    Creation,
    Bureautique,
}

/// <summary>Conseil rendu pour un adaptateur, avec le raisonnement qui l'accompagne.</summary>
public sealed record DriverAdvice(
    string AdapterName,
    string? InstalledVersion,
    DriverRelease? Installed,
    DriverRelease? Recommended,
    string Reasoning,
    IReadOnlyList<KnownIssue> IssuesOnInstalled)
{
    public bool IsUpToDate =>
        Recommended is null ||
        (InstalledVersion is not null &&
         string.Equals(InstalledVersion, Recommended.Version, StringComparison.OrdinalIgnoreCase));

    public bool InstalledIsUnknown => Installed is null;
}

/// <summary>
/// Conseille une version de pilote pour chaque adaptateur.
///
/// Le conseil croise quatre elements : la carte, la topologie de la machine, les
/// problemes connus de la version installee, et l'historique de plantage releve.
/// Recommander la version la plus recente sans regarder le reste serait un conseil
/// de catalogue, pas un conseil pour cette machine.
/// </summary>
public sealed class DriverAdvisor
{
    private readonly DriverCatalog _catalog;

    public DriverAdvisor(DriverCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
    }

    public IReadOnlyList<DriverAdvice> Advise(SystemSnapshot snapshot, UsageProfile profile = UsageProfile.Jeu)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!snapshot.Gpus.IsUsable || snapshot.Gpus.Value is not { } gpus) return [];

        var hybrid = IsHybrid(gpus);
        var crashed = HasCrashHistory(snapshot);

        return gpus
            .Where(g => !g.IsGenericMicrosoftDriver)
            .Select(gpu => Advise(gpu, hybrid, crashed, profile))
            .ToList();
    }

    private DriverAdvice Advise(GpuInfo gpu, bool hybrid, bool crashed, UsageProfile profile)
    {
        var candidates = _catalog.Releases.Where(r => r.Covers(gpu)).ToList();

        if (candidates.Count == 0)
        {
            return new DriverAdvice(gpu.Name, gpu.DriverVersion, null, null,
                "Cette carte ne figure pas dans le catalogue embarque. Aucun conseil ne peut " +
                "etre donne sans risquer d'induire en erreur : consultez le site du constructeur.",
                []);
        }

        var installed = candidates.FirstOrDefault(r =>
            string.Equals(r.Version, gpu.DriverVersion, StringComparison.OrdinalIgnoreCase));

        var recommended = Choose(candidates, hybrid, crashed, profile);
        var issues = installed?.KnownIssues ?? [];

        return new DriverAdvice(gpu.Name, gpu.DriverVersion, installed, recommended,
            Explain(gpu, installed, recommended, hybrid, crashed, profile), issues);
    }

    /// <summary>
    /// Choisit la version conseillee. Une version portant un probleme grave est
    /// ecartee quoi qu'il arrive ; sur configuration hybride ou apres des plantages,
    /// la stabilite prime sur la nouveaute.
    /// </summary>
    private static DriverRelease? Choose(
        IReadOnlyList<DriverRelease> candidates, bool hybrid, bool crashed, UsageProfile profile)
    {
        var eligible = candidates.Where(r => !r.HasSeriousIssue && r.Verdict != Verdict.AEviter).ToList();
        if (eligible.Count == 0) return null;

        // En creation, la branche Studio est privilegiee : elle est validee sur des
        // cycles plus longs, au prix de fonctionnalites de jeu plus tardives.
        if (profile == UsageProfile.Creation)
        {
            var studio = eligible.Where(r => r.Branch == DriverBranch.Studio).ToList();
            if (studio.Count > 0) eligible = studio;
        }

        // Configuration hybride ou historique de plantage : on ecarte aussi les
        // versions portant un probleme seulement notable, car elles ajouteraient de
        // l'incertitude a une machine qui en a deja.
        if (hybrid || crashed)
        {
            var conservative = eligible.Where(r => r.KnownIssues.Count == 0).ToList();
            if (conservative.Count > 0) eligible = conservative;
        }

        return eligible
            .OrderByDescending(r => r.Verdict == Verdict.Recommande)
            .ThenByDescending(r => r.ReleaseDate)
            .First();
    }

    private static string Explain(
        GpuInfo gpu, DriverRelease? installed, DriverRelease? recommended,
        bool hybrid, bool crashed, UsageProfile profile)
    {
        if (recommended is null)
        {
            return "Aucune version du catalogue ne convient a cette carte sans reserve. " +
                   "Consultez le site du constructeur.";
        }

        var reasons = new List<string>();

        if (installed is null)
        {
            reasons.Add(
                $"La version installee ({gpu.DriverVersion ?? "inconnue"}) ne figure pas au catalogue : " +
                "elle est soit plus recente que celui-ci, soit specifique a votre constructeur.");
        }
        else if (installed.Version == recommended.Version)
        {
            reasons.Add("La version installee est celle que nous conseillons pour cette machine.");
        }
        else if (installed.HasSeriousIssue)
        {
            reasons.Add("La version installee porte un probleme grave connu : un changement est conseille.");
        }
        else if (installed.KnownIssues.Count > 0)
        {
            reasons.Add("La version installee porte des problemes connus, sans gravite majeure.");
        }
        else
        {
            reasons.Add("La version installee ne porte aucun probleme connu.");
        }

        if (hybrid)
        {
            reasons.Add(
                "Cette machine possede deux processeurs graphiques : la stabilite a ete privilegiee " +
                "sur la nouveaute, les configurations hybrides etant plus sensibles aux regressions.");
        }

        if (crashed)
        {
            reasons.Add(
                "Des incidents graphiques ont ete releves : seules les versions sans probleme connu " +
                "ont ete retenues.");
        }

        reasons.Add(profile switch
        {
            UsageProfile.Creation =>
                "Profil creation : la branche Studio est privilegiee, validee sur des cycles plus longs.",
            UsageProfile.Bureautique =>
                "Profil bureautique : une version eprouvee suffit, les nouveautes de jeu n'apportent rien.",
            _ =>
                "Profil jeu : la version conseillee est la plus recente parmi celles jugees sures.",
        });

        return string.Join(" ", reasons);
    }

    private static bool IsHybrid(IReadOnlyList<GpuInfo> gpus) =>
        gpus.Any(g => g.Vendor == GpuVendor.Intel) &&
        gpus.Any(g => g.Vendor is GpuVendor.Nvidia or GpuVendor.Amd);

    private static bool HasCrashHistory(SystemSnapshot snapshot) =>
        (snapshot.GpuEvents.IsUsable && snapshot.GpuEvents.Value is { Count: > 0 }) ||
        (snapshot.CrashArtifacts.IsUsable && snapshot.CrashArtifacts.Value is { Count: > 0 });
}
