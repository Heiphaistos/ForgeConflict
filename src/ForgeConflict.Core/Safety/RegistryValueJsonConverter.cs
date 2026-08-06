using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ForgeConflict.Core.Safety;

/// <summary>
/// Conserve le type des valeurs de registre a travers la serialisation.
///
/// Sans ce convertisseur, System.Text.Json relit tout <c>object?</c> sous forme de
/// <see cref="JsonElement"/>. La valeur precedente d'une transaction relue apres
/// redemarrage n'etait donc plus un entier, et la restauration ecrivait un objet
/// inexploitable dans le registre : le mecanisme de reprise, qui est la garantie
/// centrale du projet, ne fonctionnait pas.
///
/// Le type est ecrit explicitement a cote de la valeur, sous la forme
/// <c>{ "type": "i32", "value": 8 }</c>, pour survivre a l'aller-retour.
/// </summary>
public sealed class RegistryValueJsonConverter : JsonConverter<object?>
{
    private const string TypeProperty = "type";
    private const string ValueProperty = "value";

    public override object? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            // Forme heritee ou inattendue : on la lit telle quelle plutot que
            // d'echouer, quitte a perdre le type. Une transaction lisible vaut
            // mieux qu'une transaction rejetee.
            using var fallback = JsonDocument.ParseValue(ref reader);
            return ReadLoose(fallback.RootElement);
        }

        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;

        if (!root.TryGetProperty(TypeProperty, out var type) ||
            !root.TryGetProperty(ValueProperty, out var value))
        {
            return ReadLoose(root);
        }

        return type.GetString() switch
        {
            "i32" => value.GetInt32(),
            "i64" => value.GetInt64(),
            "str" => value.GetString(),
            "bool" => value.GetBoolean(),
            "strs" => value.EnumerateArray().Select(e => e.GetString() ?? string.Empty).ToArray(),
            _ => ReadLoose(value),
        };
    }

    public override void Write(Utf8JsonWriter writer, object? value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();

        switch (value)
        {
            case int i:
                writer.WriteString(TypeProperty, "i32");
                writer.WriteNumber(ValueProperty, i);
                break;

            case long l:
                writer.WriteString(TypeProperty, "i64");
                writer.WriteNumber(ValueProperty, l);
                break;

            case bool b:
                writer.WriteString(TypeProperty, "bool");
                writer.WriteBoolean(ValueProperty, b);
                break;

            case string[] array:
                writer.WriteString(TypeProperty, "strs");
                writer.WriteStartArray(ValueProperty);
                foreach (var item in array) writer.WriteStringValue(item);
                writer.WriteEndArray();
                break;

            default:
                writer.WriteString(TypeProperty, "str");
                writer.WriteString(ValueProperty,
                    Convert.ToString(value, CultureInfo.InvariantCulture));
                break;
        }

        writer.WriteEndObject();
    }

    /// <summary>Lecture de secours quand le type n'a pas ete conserve.</summary>
    private static object? ReadLoose(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Number => element.TryGetInt32(out var i) ? i : element.GetInt64(),
        JsonValueKind.String => element.GetString(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => element.ToString(),
    };
}
