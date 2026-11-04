using System.ComponentModel;
using System.Windows;
using ForgeConflict.App.ViewModels;
using ForgeConflict.App.Views;
using ForgeConflict.Core.Safety;
using Wpf.Ui.Controls;

namespace ForgeConflict.App;

public partial class MainWindow : FluentWindow
{
    public MainWindow(ShellViewModel viewModel, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(services);

        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();

        // SetPageProviderService et non SetServiceProvider : c'est ce fournisseur que
        // NavigationView interroge lorsqu'un element de navigation est active. Sans lui
        // le clic reste sans effet, sans erreur visible.
        Navigation.SetPageProviderService(new ServicePageProvider(services));
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    public ShellViewModel ViewModel { get; }

    private bool _exitPending;

    private void OnLoaded(object sender, RoutedEventArgs e) =>
        Navigation.Navigate(typeof(DashboardPage));

    /// <summary>
    /// Croix ou Alt+F4 pendant une operation sur les pilotes : confirmation, puis
    /// « Fermer quand meme » masque la fenetre et quitte a la fin de la derniere
    /// operation, qui n'est donc jamais coupee.
    /// </summary>
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (InstallTracker.CurrentName is not { } busy) return;

        e.Cancel = true;
        if (_exitPending || !InstallCloseDialog.Ask(busy, this)) return;

        _exitPending = true;
        Hide();
        InstallTracker.Idle += () => Dispatcher.BeginInvoke(ExitIfIdle);
        ExitIfIdle();
    }

    private void ExitIfIdle()
    {
        if (!InstallTracker.IsBusy) Application.Current.Shutdown();
    }
}
