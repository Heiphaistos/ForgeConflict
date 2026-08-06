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

    public RemediationService(ITransactionStore store, IChangeApplier applier, ILog log)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(applier);
        ArgumentNullException.ThrowIfNull(log);

        _store = store;
        _applier = applier;
        _log = log;
    }

    public static IReadOnlyList<IRemediation> DefaultRemediations() =>
    [
        new RestoreTdrDefaultsRemediation(),
    ];

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
            return new RemediationResult(RemediationOutcome.Appliquee, result.Message);
        }

        var watchdog = new DisplayWatchdog(_log);
        var verdict = await watchdog
            .WatchAsync(watchdogDelay ?? DisplayWatchdog.DefaultDelay, confirmDisplay())
            .ConfigureAwait(true);

        if (verdict == WatchdogOutcome.Confirme)
        {
            return new RemediationResult(RemediationOutcome.Appliquee,
                remediation.RequiresReboot
                    ? "Modification appliquee. Elle prendra effet apres un redemarrage."
                    : "Modification appliquee et confirmee.");
        }

        runner.Rollback(transaction);

        return new RemediationResult(RemediationOutcome.AnnuleeParWatchdog,
            verdict == WatchdogOutcome.DelaiDepasse
                ? "Aucune confirmation recue : l'etat precedent a ete retabli automatiquement."
                : "Modification annulee a votre demande : l'etat precedent a ete retabli.");
    }
}
