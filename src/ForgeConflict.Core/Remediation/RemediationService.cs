using ForgeConflict.Core.Inventory;
using ForgeConflict.Core.Logging;
using ForgeConflict.Core.Rules;
using ForgeConflict.Core.Safety;

namespace ForgeConflict.Core.Remediation;

public enum RemediationOutcome
{
    /// <summary>L'utilisateur n'a pas donne son accord : rien n'a ete touche.</summary>
    Refusee,

    /// <summary>Appliquee et confirmee apres coup.</summary>
    Appliquee,

    /// <summary>Appliquee puis defaite : le watchdog n'a pas recu de confirmation.</summary>
    AnnuleeParWatchdog,

    /// <summary>L'application a echoue ; l'etat precedent a ete retabli.</summary>
    Echouee,

    /// <summary>Il n'y avait rien a modifier sur cette machine.</summary>
    SansEffet,
}

public sealed record RemediationResult(RemediationOutcome Outcome, string Message);

/// <summary>
/// Orchestre une correction de bout en bout : apercu, accord explicite, transaction
/// journalisee, puis surveillance de l'affichage.
///
/// Aucune correction ne s'applique sans accord : la demande de confirmation est un
/// parametre obligatoire, pas une option qu'on peut oublier de fournir.
/// </summary>
public sealed class RemediationService
{
    private readonly ITransactionStore _store;
    private readonly IChangeApplier _applier;
    private readonly ILog _log;
    private readonly SystemRestorePoint? _restorePoint;

    /// <param name="restorePoint">
    /// Facultatif : sans lui, aucun point de restauration n'est cree. Les tests le
    /// laissent de cote pour ne pas solliciter la protection systeme de la machine
    /// de developpement.
    /// </param>
    public RemediationService(
        ITransactionStore store,
        IChangeApplier applier,
        ILog log,
        SystemRestorePoint? restorePoint = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(applier);
        ArgumentNullException.ThrowIfNull(log);

        _store = store;
        _applier = applier;
        _log = log;
        _restorePoint = restorePoint;
    }

    public static IReadOnlyList<IRemediation> DefaultRemediations() =>
    [
        new RestoreTdrDefaultsRemediation(),
        new RemoveStaleDriverPackageRemediation(),
    ];

    /// <summary>
    /// Applicateur couvrant tous les types de modification connus. Chaque type est
    /// route vers le code qui sait le traiter.
    /// </summary>
    public static IChangeApplier BuildApplier(ILog log)
    {
        ArgumentNullException.ThrowIfNull(log);

        return new CompositeChangeApplier(
            new Dictionary<ChangeKind, IChangeApplier>
            {
                [ChangeKind.RegistryValue] = new RegistryChangeApplier(log),
                [ChangeKind.ScheduledTask] = new DriverStoreChangeApplier(log),
            },
            log);
    }

    public static IRemediation? For(Finding finding, IReadOnlyList<IRemediation>? catalogue = null)
    {
        ArgumentNullException.ThrowIfNull(finding);
        return (catalogue ?? DefaultRemediations()).FirstOrDefault(r => r.Handles(finding));
    }

    /// <param name="confirm">
    /// Demande l'accord de l'utilisateur, apercu des changements a l'appui. Sans
    /// reponse positive, rien n'est modifie.
    /// </param>
    /// <param name="confirmDisplay">
    /// Demande une confirmation apres coup lorsque l'affichage est concerne. Une
    /// absence de reponse dans le delai vaut refus et declenche le retour arriere.
    /// </param>
    public async Task<RemediationResult> ApplyAsync(
        IRemediation remediation,
        SystemSnapshot snapshot,
        Func<IReadOnlyList<ChangeRecord>, Task<bool>> confirm,
        Func<Task<bool>>? confirmDisplay = null,
        TimeSpan? watchdogDelay = null)
    {
        ArgumentNullException.ThrowIfNull(remediation);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(confirm);

        var changes = remediation.Preview(snapshot);

        if (changes.Count == 0)
        {
            return new RemediationResult(RemediationOutcome.SansEffet,
                "Rien a modifier : cette machine est deja dans l'etat vise.");
        }

        if (!await confirm(changes).ConfigureAwait(true))
        {
            _log.Write(LogLevel.Info, $"correction refusee par l'utilisateur : {remediation.Title}");
            return new RemediationResult(RemediationOutcome.Refusee, "Aucune modification effectuee.");
        }

        // Le point de restauration precede l'ecriture pour les risques les plus
        // eleves. Son echec n'annule pas la correction — la protection systeme est
        // souvent desactivee — mais il est signale dans le message final : le
        // journal transactionnel devient alors le seul filet.
        var restoreNote = string.Empty;

        if (remediation.Risk >= RiskLevel.Eleve && _restorePoint is not null)
        {
            var restore = _restorePoint.Create($"ForgeConflict — {remediation.Title}");
            if (!restore.Created) restoreNote = " " + restore.Message;
        }

        var transaction = remediation.BuildTransaction(snapshot);
        var runner = new TransactionRunner(_store, _applier, _log);
        var result = runner.Run(transaction);

        if (!result.Succeeded)
        {
            return new RemediationResult(RemediationOutcome.Echouee, result.Message);
        }

        // Le watchdog n'a de sens que si l'affichage peut etre perturbe ET si
        // l'appelant sait poser la question. Sans interlocuteur, appliquer puis
        // annuler aussitot serait absurde.
        if (!remediation.AffectsDisplay || confirmDisplay is null)
        {
            return new RemediationResult(RemediationOutcome.Appliquee, result.Message + restoreNote);
        }

        var watchdog = new DisplayWatchdog(_log);
        var verdict = await watchdog
            .WatchAsync(watchdogDelay ?? DisplayWatchdog.DefaultDelay, confirmDisplay())
            .ConfigureAwait(true);

        if (verdict == WatchdogOutcome.Confirme)
        {
            return new RemediationResult(RemediationOutcome.Appliquee,
                (remediation.RequiresReboot
                    ? "Modification appliquee. Elle prendra effet apres un redemarrage."
                    : "Modification appliquee et confirmee.") + restoreNote);
        }

        runner.Rollback(transaction);

        return new RemediationResult(RemediationOutcome.AnnuleeParWatchdog,
            verdict == WatchdogOutcome.DelaiDepasse
                ? "Aucune confirmation recue : l'etat precedent a ete retabli automatiquement."
                : "Modification annulee a votre demande : l'etat precedent a ete retabli.");
    }
}
