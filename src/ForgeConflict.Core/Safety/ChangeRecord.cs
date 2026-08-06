using System.Text.Json.Serialization;

namespace ForgeConflict.Core.Safety;

public enum ChangeKind
{
    RegistryValue,
    ServiceStartMode,
    ScheduledTask,
}

/// <summary>
/// Une modification unitaire, avec de quoi la defaire. La valeur precedente est
/// conservee telle quelle, y compris nulle : nulle signifie que la valeur n'existait
/// pas et que la restauration doit la SUPPRIMER, non y ecrire zero.
/// </summary>
public sealed record ChangeRecord(
    ChangeKind Kind,
    string Target,
    string? ValueName,
    // Le convertisseur conserve le type reel a travers la serialisation. Sans lui,
    // une valeur relue apres redemarrage serait un JsonElement et la restauration
    // ecrirait un objet inexploitable dans le registre.
    [property: JsonConverter(typeof(RegistryValueJsonConverter))]
    object? PreviousValue,
    [property: JsonConverter(typeof(RegistryValueJsonConverter))]
    object? NewValue,
    bool PreviousValueExisted,
    bool AffectsDisplay)
{
    public static ChangeRecord RegistryValue(
        string keyPath,
        string valueName,
        object? previousValue,
        object? newValue,
        bool affectsDisplay = true) =>
        new(ChangeKind.RegistryValue, keyPath, valueName, previousValue, newValue,
            PreviousValueExisted: previousValue is not null,
            AffectsDisplay: affectsDisplay);

    public string Describe() => Kind switch
    {
        ChangeKind.RegistryValue => PreviousValueExisted
            ? $"{Target}\\{ValueName} : {PreviousValue} -> {NewValue}"
            : $"{Target}\\{ValueName} : absente -> {NewValue}",
        ChangeKind.ServiceStartMode => $"service {Target} : {PreviousValue} -> {NewValue}",
        _ => $"{Target} : {PreviousValue} -> {NewValue}",
    };
}
