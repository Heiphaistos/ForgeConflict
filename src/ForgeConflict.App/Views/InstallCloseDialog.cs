using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ForgeConflict.App.Views;

/// <summary>
/// Boîte « Installation en cours » affichée quand on ferme ForgeConflict pendant une
/// installation. Bouton par défaut (Entrée, Échap, croix) : « Attendre ».
/// </summary>
public static class InstallCloseDialog
{
    public const string Title     = "Installation en cours";
    public const string CloseText = "Fermer quand même";
    public const string WaitText  = "Attendre";

    public static string Message(string operation) =>
        $"{operation} est en cours. Fermer maintenant ? L'installation se terminera en arrière-plan.";

    /// <summary>Vrai si l'utilisateur choisit « Fermer quand même ».</summary>
    public static bool Ask(string operation, Window? owner)
    {
        bool close = false;
        var win = new Window
        {
            Title                 = Title,
            Width                 = 480,
            SizeToContent         = SizeToContent.Height,
            MinHeight             = 0,
            ResizeMode            = ResizeMode.NoResize,
            ShowInTaskbar         = owner is null || !owner.IsVisible,
            WindowStartupLocation = owner is { IsVisible: true } ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            Owner                 = owner is { IsVisible: true } ? owner : null,
        };
        win.SetResourceReference(Control.BackgroundProperty, "Surface");
        win.SetResourceReference(Control.ForegroundProperty, "TexteFort");

        var text = new TextBlock
        {
            Text         = Message(operation),
            TextWrapping = TextWrapping.Wrap,
            FontSize     = 14,
            Margin       = new Thickness(0, 0, 0, 18),
        };
        text.SetResourceReference(TextBlock.ForegroundProperty, "TexteFort");

        var closeBtn = new Wpf.Ui.Controls.Button { Content = CloseText, Margin = new Thickness(0, 0, 10, 0), Padding = new Thickness(16, 7, 16, 7) };
        var waitBtn  = new Wpf.Ui.Controls.Button
        {
            Content    = WaitText,
            IsDefault  = true,
            IsCancel   = true,
            Appearance = Wpf.Ui.Controls.ControlAppearance.Primary,
            Padding    = new Thickness(16, 7, 16, 7),
        };
        closeBtn.Click += (_, _) => { close = true; win.Close(); };
        waitBtn.Click  += (_, _) => win.Close();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(closeBtn);
        buttons.Children.Add(waitBtn);

        var root = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
        root.Children.Add(text);
        root.Children.Add(buttons);
        win.Content = root;
        win.Loaded += (_, _) => Keyboard.Focus(waitBtn);
        // Barre de titre sombre comme la fenêtre principale (DWMWA_USE_IMMERSIVE_DARK_MODE).
        win.SourceInitialized += (_, _) =>
        {
            int dark = 1;
            DwmSetWindowAttribute(new System.Windows.Interop.WindowInteropHelper(win).Handle, 20, ref dark, sizeof(int));
        };

        win.ShowDialog();
        return close;
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}
