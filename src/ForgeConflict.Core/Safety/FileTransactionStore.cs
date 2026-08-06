using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ForgeConflict.Core.Safety;

/// <summary>
/// Journal sur disque, un fichier par transaction. Le decoupage en fichiers isole les
/// degats : une ecriture interrompue n'abime que sa propre entree, jamais les autres.
/// </summary>
public sealed class FileTransactionStore : ITransactionStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _directory;

    public FileTransactionStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory;
    }

    public void Write(Transaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        Directory.CreateDirectory(_directory);
        var path = PathFor(transaction.Id);
        var temporary = path + ".tmp";

        // Ecriture puis remplacement atomique : un fichier definitif n'est jamais
        // observe a moitie ecrit, meme si le courant tombe pendant l'operation.
        File.WriteAllText(temporary, JsonSerializer.Serialize(transaction, Options), Encoding.UTF8);
        File.Move(temporary, path, overwrite: true);
    }

    public IReadOnlyList<Transaction> ReadAll()
    {
        if (!Directory.Exists(_directory)) return [];

        var transactions = new List<Transaction>();

        foreach (var path in Directory.EnumerateFiles(_directory, "*.json"))
        {
            try
            {
                var transaction = JsonSerializer.Deserialize<Transaction>(
                    File.ReadAllText(path), Options);

                if (transaction is not null) transactions.Add(transaction);
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                // Entree tronquee par une coupure : on l'ignore plutot que d'empecher
                // la lecture des transactions saines, dont certaines attendent
                // peut-etre une restauration.
            }
        }

        return transactions;
    }

    public IReadOnlyList<Transaction> ReadPending() =>
        ReadAll().Where(t => t.NeedsRecovery).ToList();

    private string PathFor(Guid id) => Path.Combine(_directory, $"{id:N}.json");
}
