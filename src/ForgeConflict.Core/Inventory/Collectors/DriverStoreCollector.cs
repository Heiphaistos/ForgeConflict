using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Xml.Linq;
using ForgeConflict.Core.Inventory.Model;

namespace ForgeConflict.Core.Inventory.Collectors;

/// <summary>
/// Enumere les paquets de pilote d'affichage du magasin. La sortie XML de pnputil est
/// utilisee et non sa sortie texte : cette derniere est traduite dans la langue du
/// systeme et son analyse casserait hors d'un Windows francais.
/// </summary>
public sealed class DriverStoreCollector : ICollector<IReadOnlyList<DriverPackage>>
{
    private const string DisplayClassGuid = "{4d36e968-e325-11ce-bfc1-08002be10318}";

    public string Source => "magasin-pilotes";

    public TimeSpan Timeout => TimeSpan.FromSeconds(45);

    public IReadOnlyList<DriverPackage> Collect(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ParseXml(RunPnpUtil(cancellationToken));
    }

    private static string RunPnpUtil(CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo
        {
            FileName = "pnputil.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        info.ArgumentList.Add("/enum-drivers");
        info.ArgumentList.Add("/class");
        info.ArgumentList.Add(DisplayClassGuid);
        info.ArgumentList.Add("/format");
        info.ArgumentList.Add("xml");

        using var process = Process.Start(info);
        if (process is null) return string.Empty;

        var output = process.StandardOutput.ReadToEnd();

        // Le processus est tue s'il ne rend pas la main : un pnputil bloque ne doit pas
        // immobiliser le scan.
        if (!process.WaitForExit(30_000))
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            return string.Empty;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return output;
    }

    /// <summary>Analyse la sortie XML. Un document illisible rend une liste vide, jamais une exception.</summary>
    public static IReadOnlyList<DriverPackage> ParseXml(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return [];

        XDocument document;
        try
        {
            document = XDocument.Parse(xml);
        }
        catch (System.Xml.XmlException)
        {
            return [];
        }

        var packages = new List<DriverPackage>();

        foreach (var node in document.Descendants("Driver"))
        {
            // pnputil expose le nom publie en ATTRIBUT DriverName, pas en element.
            // L'element PublishedName est accepte en repli pour les variantes de format.
            var published = node.Attribute("DriverName")?.Value?.Trim()
                            ?? Value(node, "PublishedName");
            if (string.IsNullOrWhiteSpace(published)) continue;

            var (date, version) = SplitDriverVersion(Value(node, "DriverVersion"));

            packages.Add(new DriverPackage(
                PublishedName: published,
                OriginalName: Value(node, "OriginalName"),
                ProviderName: Value(node, "ProviderName"),
                ClassName: Value(node, "ClassName"),
                ClassGuid: Value(node, "ClassGuid"),
                DriverVersion: version,
                DriverDate: date,
                // SignerName absent signifie non signe : la chaine vide est normalisee
                // en null pour que IsUnsigned reste vrai dans les deux cas.
                SignerName: NullIfBlank(Value(node, "SignerName"))));
        }

        return packages;
    }

    private static string? Value(XElement node, string name) => node.Element(name)?.Value?.Trim();

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>
    /// pnputil concatene date et version dans un seul champ, ex. "12/30/2025 32.0.15.9174",
    /// et la date y est toujours au format americain quelle que soit la langue.
    /// </summary>
    private static (DateTimeOffset? Date, string? Version) SplitDriverVersion(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return (null, null);

        var parts = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2) return (null, raw);

        DateTimeOffset? date = DateTime.TryParseExact(
            parts[0], "MM/dd/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? new DateTimeOffset(parsed, TimeSpan.Zero)
            : null;

        return (date, parts[1]);
    }
}
