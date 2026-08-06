using System.Windows;
using ForgeConflict.App.ViewModels;
using ForgeConflict.App.Views;
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
    }

    public ShellViewModel ViewModel { get; }

    private void OnLoaded(object sender, RoutedEventArgs e) =>
        Navigation.Navigate(typeof(DashboardPage));
}
