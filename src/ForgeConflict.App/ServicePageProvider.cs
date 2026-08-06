using System.Windows;
using Wpf.Ui.Abstractions;

namespace ForgeConflict.App;

/// <summary>
/// Fournit les pages a la navigation depuis le conteneur d'injection.
///
/// Sans ce fournisseur, NavigationView tente d'instancier les pages par leur
/// constructeur sans parametre. Les notres reclament le modele partage : la
/// construction echoue en silence et le clic sur un element de navigation
/// ne produit simplement aucun effet.
/// </summary>
public sealed class ServicePageProvider : INavigationViewPageProvider
{
    private readonly IServiceProvider _services;

    public ServicePageProvider(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
    }

    public object? GetPage(Type pageType)
    {
        ArgumentNullException.ThrowIfNull(pageType);

        // GetService et non GetRequiredService : une page non enregistree doit
        // laisser la navigation reprendre la main, pas faire tomber l'application.
        return _services.GetService(pageType) as FrameworkElement;
    }
}
