using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ForgeConflict.Core.Collection;
using ForgeConflict.Core.Inventory;
using ForgeConflict.Core.Logging;
using ForgeConflict.Core.Rules;

namespace ForgeConflict.App.ViewModels;

/// <summary>
/// Etat partage par les pages : un seul scan alimente le tableau de bord,
/// l'inventaire et le journal.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly ILog _log;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private SystemSnapshot? _snapshot;

    [ObservableProperty]
    private HealthLevel _overall = HealthLevel.Inconnu;

    [ObservableProperty]
    private string _report = "Aucune analyse effectuee. Lancez une analyse depuis le tableau de bord.";

    [ObservableProperty]
    private RuleReport? _rules;

    public ObservableCollection<SourceHealth> Sources { get; } = [];

    public ShellViewModel(ILog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        _log = log;
    }

    public async Task ScanAsync()
    {
        if (IsScanning) return;

        IsScanning = true;
        try
        {
            var scanner = new InventoryScanner(new IsolatedRunner(_log));
            var snapshot = await scanner.ScanAsync().ConfigureAwait(true);

            Snapshot = snapshot;
            Report = SnapshotReport.Render(snapshot);

            Sources.Clear();
            foreach (var fragment in snapshot.AllFragments)
            {
                Sources.Add(SourceHealth.FromFragment(fragment));
            }

            Overall = SourceHealth.Overall(Sources.Select(s => s.Level).ToList());

            // Les regles sont evaluees dans la foulee : elles lisent l'instantane
            // deja en memoire, sans reinterroger la machine.
            Rules = new RuleEngine(_log).Evaluate(snapshot);

            _log.Write(LogLevel.Info,
                $"analyse terminee, etat global {Overall}, {snapshot.UnusableSources.Count} source(s) indisponible(s), " +
                $"{Rules.Problems.Count} probleme(s) detecte(s)");
        }
        finally
        {
            IsScanning = false;
        }
    }
}
