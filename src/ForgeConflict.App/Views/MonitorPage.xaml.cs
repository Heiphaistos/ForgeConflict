using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ForgeConflict.App.ViewModels;
using ForgeConflict.Core.Monitoring;
using ForgeConflict.Core.Rules;

namespace ForgeConflict.App.Views;

public partial class MonitorPage : Page
{
    private readonly ShellViewModel _viewModel;
    private readonly ObservableCollection<EventLine> _lines = [];

    public MonitorPage(ShellViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        _viewModel = viewModel;
        InitializeComponent();

        Evenements.ItemsSource = _lines;

        foreach (var observed in _viewModel.LiveEvents) _lines.Add(new EventLine(observed));

        _viewModel.LiveEventObserved += OnObserved;
        Unloaded += (_, _) => _viewModel.LiveEventObserved -= OnObserved;

        RefreshState();
    }

    private void OnObserved(object? sender, LiveEvent observed)
    {
        // L'evenement arrive depuis un fil de WMI : il faut repasser sur le fil de
        // l'interface avant de toucher a la collection liee.
        Dispatcher.Invoke(() =>
        {
            _lines.Insert(0, new EventLine(observed));

            // La liste est bornee : une surveillance laissee active des heures ne
            // doit pas consommer la memoire sans limite.
            while (_lines.Count > 500) _lines.RemoveAt(_lines.Count - 1);
        });
    }

    private void OnToggle(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsMonitoring) _viewModel.StopMonitoring();
        else _viewModel.StartMonitoring();

        RefreshState();
    }

    private void OnClear(object sender, RoutedEventArgs e)
    {
        _lines.Clear();
        _viewModel.ClearLiveEvents();
    }

    private void RefreshState()
    {
        Basculer.Content = _viewModel.IsMonitoring
            ? "Arreter la surveillance"
            : "Demarrer la surveillance";

        Etat.Text = _viewModel.IsMonitoring
            ? "Surveillance active."
            : "Surveillance arretee.";
    }

    private sealed class EventLine
    {
        public EventLine(LiveEvent observed)
        {
            Time = observed.At.ToString("HH:mm:ss");
            Message = observed.Message;

            var key = observed.Severity switch
            {
                >= Severity.Eleve => "Critique",
                Severity.Moyen => "Attention",
                _ => "Inconnu",
            };

            Brush = Application.Current.TryFindResource(key) as Brush ?? Brushes.Gray;
        }

        public string Time { get; }
        public string Message { get; }
        public Brush Brush { get; }
    }
}
