using System.Text.Json;
using System.Text.Json.Serialization;

namespace ForgeConflict.Core.Collection;

public static class SnapshotSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize(SnapshotEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        return JsonSerializer.Serialize(envelope, Options);
    }

    /// <exception cref="InvalidDataException">
    /// Le JSON est illisible ou porte une version de schema non supportee.
    /// </exception>
    public static SnapshotEnvelope Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        SnapshotEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<SnapshotEnvelope>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("L'instantane est illisible.", ex);
        }

        if (envelope is null)
        {
            throw new InvalidDataException("L'instantane est vide.");
        }

        if (envelope.SchemaVersion != SnapshotEnvelope.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Version de schema {envelope.SchemaVersion} non supportee, attendu {SnapshotEnvelope.CurrentSchemaVersion}.");
        }

        return envelope;
    }
}
