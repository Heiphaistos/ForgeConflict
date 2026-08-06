using System.Windows.Controls;
using ForgeConflict.App.ViewModels;

namespace ForgeConflict.App.Views;

public partial class InventoryPage : Page
{
    public InventoryPage(ShellViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();
        Rapport.Text = viewModel.Report;
    }
}
