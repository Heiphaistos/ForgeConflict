using System.Diagnostics.Eventing.Reader;
using System.Management;
using ForgeConflict.Core.Inventory.Collectors;
using ForgeConflict.Core.Logging;
using ForgeConflict.Core.Rules;

namespace ForgeConflict.Core.Monitoring;

public enum LiveEventKind
{
    OverlayDemarre,
    OverlayArrete,
    IncidentGraphique,
}

public sealed record LiveEvent(
    DateTimeOffset At,
    LiveEventKind Kind,
    string Subject,
    string Message,
    Severity Severity);

/// <summary>
/// Surveille les evenements graphiques en direct : lancement d'une superposition,
/// reinitialisation du pilote.
///
/// La surveillance NOTIFIE, elle n'agit jamais d'elle-meme. Momo a explicitement
/// exclu toute correction automatique : ce service n'a aucun acces en ecriture.
/// </summary>
public sealed class LiveMonitor : IDisposable
{
    private readonly ILog _log;
    private ManagementEventWatcher? _processStart;
    private ManagementEventWatcher? _processStop;
    private EventLogWatcher? _eventLog;
    private bool _running;

    public LiveMonitor(ILog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        _log = log;
    }

    public event EventHandler<LiveEvent>? Observed;

    public bool IsRunning => _running;

    public void Start()
    {
        if (_running) return;

        StartProcessWatchers();
        StartEventLogWatcher();

        _running = true;
        _log.Write(LogLevel.Info, "surveillance temps reel demarree");
    }

    public void Stop()
    {
        if (!_running) return;

        Dispose(disposing: true);
        _running = false;
        _log.Write(LogLevel.Info, "surveillance temps reel arretee");
    }

    private void StartProcessWatchers()
    {
        try
        {
            // WITHIN 2 : la fenetre de sondage de WMI. Plus court sollicite le systeme
            // sans profit visible pour un usage de surveillance.
            _processStart = Watch(
                "SELECT * FROM __InstanceCreationEvent WITHIN 2 WHERE TargetInstance ISA 'Win32_Process'",
                LiveEventKind.OverlayDemarre);

            _processStop = Watch(
                "SELECT * FROM __InstanceDeletionEvent WITHIN 2 WHERE TargetInstance ISA 'Win32_Process'",
                LiveEventKind.OverlayArrete);
        }
        catch (ManagementException ex)
        {
            // Sans surveillance des processus, celle du journal reste utile : on
            // degrade plutot que d'abandonner la fonction entiere.
            _log.Write(LogLevel.Warn, $"surveillance des processus indisponible : {ex.Message}");
        }
    }

    private ManagementEventWatcher Watch(string query, LiveEventKind kind)
    {
        var watcher = new ManagementEventWatcher(new WqlEventQuery(query));

        watcher.EventArrived += (_, args) =>
        {
            try
            {
                using var target = (ManagementBaseObject)args.NewEvent["TargetInstance"];
                var name = target["Name"]?.ToString();

                if (name is null || !OverlayProcessCollector.IsKnownOverlay(name)) return;

                var started = kind == LiveEventKind.OverlayDemarre;

                Raise(new LiveEvent(
                    DateTimeOffset.Now, kind, name,
                    started
                        ? $"{name} vient de demarrer et s'accroche au rendu graphique."
                        : $"{name} s'est arrete.",
                    Severity.Information));
            }
            catch (ManagementException)
            {
                // Le processus a disparu entre la notification et la lecture.
            }
        };

        watcher.Start();
        return watcher;
    }

    private void StartEventLogWatcher()
    {
        try
        {
            var query = new EventLogQuery("System", PathType.LogName, GpuEventCollector.BuildQuery());
            _eventLog = new EventLogWatcher(query);

            _eventLog.EventRecordWritten += (_, args) =>
            {
                if (args.EventRecord is not { } record) return;

                using (record)
                {
                    Raise(new LiveEvent(
                        DateTimeOffset.Now, LiveEventKind.IncidentGraphique,
                        record.ProviderName ?? "inconnu",
                        record.Id is 4101 or 4102
                            ? "Le pilote graphique vient d'etre reinitialise apres un blocage."
                            : $"Incident graphique signale par {record.ProviderName} (evenement {record.Id}).",
                        Severity.Eleve));
                }
            };

            _eventLog.Enabled = true;
        }
        catch (EventLogException ex)
        {
            _log.Write(LogLevel.Warn, $"surveillance du journal indisponible : {ex.Message}");
        }
    }

    private void Raise(LiveEvent liveEvent)
    {
        _log.Write(
            liveEvent.Severity >= Severity.Eleve ? LogLevel.Warn : LogLevel.Info,
            $"surveillance : {liveEvent.Message}");

        Observed?.Invoke(this, liveEvent);
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (!disposing) return;

        try { _processStart?.Stop(); } catch (ManagementException) { }
        try { _processStop?.Stop(); } catch (ManagementException) { }

        _processStart?.Dispose();
        _processStop?.Dispose();

        if (_eventLog is not null)
        {
            try { _eventLog.Enabled = false; } catch (EventLogException) { }
            _eventLog.Dispose();
        }

        _processStart = null;
        _processStop = null;
        _eventLog = null;
    }
}
