using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using ForgeConflict.Core.Safety;
using Wpf.Ui.Controls;

namespace ForgeConflict.App.Views;

/// <summary>
/// Demande confirmation apres une modification touchant l'affichage, avec compte a
/// rebours visible.
///
/// La fenetre ne peut pas etre fermee sans repondre : sa croix est masquee. Fermer
/// sans choisir equivaudrait a un accord tacite, alors que l'absence de reponse doit
/// valoir refus.
/// </summary>
public partial class WatchdogWindow : FluentWindow
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly Stopwatch _elapsed = Stopwatch.StartNew();
    private readonly TimeSpan _delay;
    private readonly TaskCompletionSource<bool> _answer = new();

    public WatchdogWindow(TimeSpan delay)
    {
        _delay = delay;
        InitializeComponent();

        _timer.Tick += OnTick;
        _timer.Start();
        Refresh();
    }

    /// <summary>Se termine sur vrai si l'utilisateur conserve, faux s'il annule.</summary>
    public Task<bool> Answer => _answer.Task;

    private void OnTick(object? sender, EventArgs e)
    {
        Refresh();

        if (_elapsed.Elapsed < _delay) return;

        // Le delai est ecoule sans reponse : la fenetre se retire et le service
        // declenche le retour arriere.
        Close();
    }

    private void Refresh()
    {
        var remaining = DisplayWatchdog.Remaining(_delay, _elapsed.Elapsed);

        Compte.Text = remaining switch
        {
            0 => "Annulation en cours...",
            1 => "Annulation automatique dans 1 seconde",
            _ => $"Annulation automatique dans {remaining} secondes",
        };

        Barre.Value = Math.Max(0, 100 - (_elapsed.Elapsed.TotalMilliseconds / _delay.TotalMilliseconds * 100));
    }

    private void OnKeep(object sender, RoutedEventArgs e) => Finish(keep: true);

    private void OnRevert(object sender, RoutedEventArgs e) => Finish(keep: false);

    private void Finish(bool keep)
    {
        _timer.Stop();
        _answer.TrySetResult(keep);
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();

        // Si la fenetre disparait sans reponse explicite, la tache reste inachevee :
        // le watchdog conclut alors au delai depasse, donc au retour arriere.
        base.OnClosed(e);
    }
}
