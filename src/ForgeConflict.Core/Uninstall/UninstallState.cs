using System.Text.Json;
using System.Text.Json.Serialization;
using ForgeConflict.Core.Inventory;

namespace ForgeConflict.Core.Uninstall;

public enum UninstallPhase
{
    /// <summary>Rien en cours.</summary>
    Idle,

    /// <summary>Sauvegardes faites, sortie de mode sans echec armee, redemarrage a venir.</summary>
    Prepared,

    /// <summary>La machine devrait redemarrer en mode sans echec.</summary>
    SafeModeBooting,

    /// <summary>Nettoyage en cours dans le mode sans echec.</summary>
    Cleaning,

    /// <summary>Nettoyage fini, retour au mode normal demande.</summary>
    NormalBooting,

    /// <summary>Termine, rapport disponible.</summary>
    Report,
}

/// <summary>
/// Etat de la desinstallation, persiste sur disque pour survivre aux redemarrages.
///
/// Sans persistance, un redemarrage en mode sans echec ferait perdre le fil de
/// l'operation et laisserait la machine dans cet etat, sans personne pour la
/// ramener en mode normal.
/// </summary>
public sealed record UninstallState(
    UninstallPhase Phase,
    GpuVendor Vendor,
    DateTimeOffset StartedAt,
    IReadOnlyList<string> TargetInfPackages,
    IReadOnlyList<string> TargetServices,
    IReadOnlyList<string> RemovedItems,
    IReadOnlyList<string> Failures,
    bool WindowsUpdateBlocked,
    bool SafeBootExitArmed)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static UninstallState Start(
        GpuVendor vendor,
        IReadOnlyList<string> infPackages,
        IReadOnlyList<string> services) =>
        new(UninstallPhase.Idle, vendor, DateTimeOffset.Now,
            infPackages, services, [], [], false, false);

    public UninstallState With(UninstallPhase phase) => this with { Phase = phase };

    public UninstallState Removed(string item) =>
        this with { RemovedItems = [.. RemovedItems, item] };

    public UninstallState Failed(string item) =>
        this with { Failures = [.. Failures, item] };

    /// <summary>Vrai si une operation est en cours et attend d'etre menee a son terme.</summary>
    public bool IsInProgress => Phase is not (UninstallPhase.Idle or UninstallPhase.Report);

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    public static UninstallState? FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            return JsonSerializer.Deserialize<UninstallState>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
