using System.Diagnostics.Eventing.Reader;
using System.Text;
using ForgeConflict.Core.Inventory.Model;

namespace ForgeConflict.Core.Inventory.Collectors;

/// <summary>
/// Releve l'historique de plantage graphique dans le journal Windows.
///
/// Le filtre associe toujours un fournisseur a ses identifiants d'evenement : un
/// identifiant n'est unique que par fournisseur. Filtrer sur l'identifiant seul
/// ramenerait WindowsUpdateClient id 19 ou BTHUSB id 18, sans rapport avec le GPU.
/// </summary>
public sealed class GpuEventCollector : ICollector<IReadOnlyList<GpuEvent>>
{
    private const int MaxEvents = 200;

    /// <summary>Couples fournisseur / identifiants reellement pertinents pour le GPU.</summary>
    private static readonly (string Provider, int[] Ids)[] Sources =
    [
        // Reinitialisation du pilote apres depassement du delai (TDR).
        ("Display", [4101, 4102]),
        ("Microsoft-Windows-DxgKrnl", [4101, 4102]),
        // Erreurs remontees par le pilote noyau de chaque constructeur.
        ("nvlddmkm", []),
        ("amdkmdag", []),
        ("amdwddmg", []),
        ("igfxn", []),
        ("igdkmd64", []),
        // Erreurs materielles, notamment sur le bus PCIe.
        ("Microsoft-Windows-WHEA-Logger", []),
    ];

    public string Source => "evenements-gpu";

    public TimeSpan Timeout => TimeSpan.FromSeconds(30);

    /// <summary>Construit la requete XPath du journal. Exposee pour etre verifiee par les tests.</summary>
    public static string BuildQuery()
    {
        var clauses = new List<string>();

        foreach (var (provider, ids) in Sources)
        {
            if (ids.Length == 0)
            {
                clauses.Add($"Provider[@Name='{provider}']");
                continue;
            }

            var idClause = string.Join(" or ", ids.Select(id => $"EventID={id}"));
            clauses.Add($"Provider[@Name='{provider}'] and ({idClause})");
        }

        var builder = new StringBuilder("*[System[");
        builder.Append(string.Join(" or ", clauses.Select(c => $"({c})")));
        builder.Append("]]");
        return builder.ToString();
    }

    public IReadOnlyList<GpuEvent> Collect(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var events = new List<GpuEvent>();
        var query = new EventLogQuery("System", PathType.LogName, BuildQuery())
        {
            ReverseDirection = true, // du plus recent au plus ancien
        };

        using var reader = new EventLogReader(query);

        for (var i = 0; i < MaxEvents; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            EventRecord? record;
            try
            {
                record = reader.ReadEvent();
            }
            catch (EventLogException)
            {
                // Journal indisponible ou tronque : on rend ce qui a ete lu.
                break;
            }

            if (record is null) break;

            using (record)
            {
                string? message = null;
                try
                {
                    // Le rendu du message echoue si le fournisseur est desinstalle,
                    // ce qui arrive precisement apres une desinstallation de pilote.
                    message = record.FormatDescription();
                }
                catch (EventLogException)
                {
                }

                events.Add(new GpuEvent(
                    TimeCreated: record.TimeCreated is { } time
                        ? new DateTimeOffset(time)
                        : DateTimeOffset.MinValue,
                    EventId: record.Id,
                    ProviderName: record.ProviderName ?? "inconnu",
                    Level: record.LevelDisplayName,
                    Message: message));
            }
        }

        return events;
    }
}
