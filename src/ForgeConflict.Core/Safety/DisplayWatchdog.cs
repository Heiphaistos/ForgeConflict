using ForgeConflict.Core.Logging;

namespace ForgeConflict.Core.Safety;

public enum WatchdogOutcome
{
    Confirme,
    Refuse,
    DelaiDepasse,
}

/// <summary>
/// Exige une confirmation humaine apres une modification touchant l'affichage.
/// Sans reponse dans le delai, le changement est annule.
///
/// C'est le mecanisme que Windows emploie lors d'un changement de resolution, et pour
/// la meme raison : si l'ecran devient noir, personne ne peut cliquer sur « Conserver ».
/// L'absence de reponse doit donc valoir refus, jamais accord.
/// </summary>
public sealed class DisplayWatchdog
{
    public static readonly TimeSpan DefaultDelay = TimeSpan.FromSeconds(30);

    private readonly ILog _log;

    public DisplayWatchdog(ILog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        _log = log;
    }

    public async Task<WatchdogOutcome> WatchAsync(TimeSpan delay, Task<bool> confirmation)
    {
        ArgumentNullException.ThrowIfNull(confirmation);

        var finished = await Task.WhenAny(confirmation, Task.Delay(delay)).ConfigureAwait(false);

        if (!ReferenceEquals(finished, confirmation))
        {
            // On cesse d'attendre la reponse : elle ne viendra peut-etre jamais.
            _log.Write(LogLevel.Warn,
                $"aucune confirmation apres {delay.TotalSeconds:0} s : retour arriere automatique");
            return WatchdogOutcome.DelaiDepasse;
        }

        var accepted = await confirmation.ConfigureAwait(false);
        _log.Write(LogLevel.Info, accepted ? "changement confirme" : "changement refuse");

        return accepted ? WatchdogOutcome.Confirme : WatchdogOutcome.Refuse;
    }

    /// <summary>Secondes restantes, pour l'affichage du compte a rebours.</summary>
    public static int Remaining(TimeSpan delay, TimeSpan elapsed) =>
        Math.Max(0, (int)Math.Ceiling((delay - elapsed).TotalSeconds));
}
