using ForgeConflict.Core.Logging;

namespace ForgeConflict.Core.Safety;

/// <summary>
/// Aiguille chaque modification vers l'applicateur qui sait la traiter.
///
/// Sans cet aiguillage, une transaction melant registre et magasin de pilotes
/// echouerait a la premiere modification d'un type inattendu, laissant la machine
/// dans un etat intermediaire.
/// </summary>
public sealed class CompositeChangeApplier : IChangeApplier
{
    private readonly IReadOnlyDictionary<ChangeKind, IChangeApplier> _appliers;
    private readonly ILog _log;

    public CompositeChangeApplier(IReadOnlyDictionary<ChangeKind, IChangeApplier> appliers, ILog log)
    {
        ArgumentNullException.ThrowIfNull(appliers);
        ArgumentNullException.ThrowIfNull(log);

        _appliers = appliers;
        _log = log;
    }

    public void Apply(ChangeRecord change)
    {
        ArgumentNullException.ThrowIfNull(change);
        Resolve(change).Apply(change);
    }

    public void Revert(ChangeRecord change)
    {
        ArgumentNullException.ThrowIfNull(change);
        Resolve(change).Revert(change);
    }

    private IChangeApplier Resolve(ChangeRecord change)
    {
        if (_appliers.TryGetValue(change.Kind, out var applier)) return applier;

        _log.Write(LogLevel.Error, $"aucun applicateur pour {change.Kind} : {change.Describe()}");

        throw new NotSupportedException(
            $"Aucun applicateur ne sait traiter une modification de type {change.Kind}.");
    }
}
