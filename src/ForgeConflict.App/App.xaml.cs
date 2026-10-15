using System.Windows;
using System.Windows.Threading;
using ForgeConflict.App.Theme;
using ForgeConflict.App.ViewModels;
using ForgeConflict.App.Views;
using ForgeConflict.Core.Diagnostics;
using ForgeConflict.Core.Logging;
using ForgeConflict.Core.Safety;
using Microsoft.Extensions.DependencyInjection;

namespace ForgeConflict.App;

public partial class App : Application
{
    private FileLog? _log;
    private ExceptionGuard? _guard;
    private ServiceProvider? _services;

    internal UpdateService Updates => _services!.GetRequiredService<UpdateService>();

    internal ILog Log => _log ?? throw new InvalidOperationException("Journal non initialise.");

    protected override void OnStartup(StartupEventArgs e)
    {
        _log = new FileLog(AppPaths.LogDirectory, AppPaths.LogRotationBytes, () => DateTimeOffset.Now);
        _guard = new ExceptionGuard(_log);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        _log.Write(LogLevel.Info, $"demarrage de ForgeConflict {ThisAssembly.Version}");

        var collection = new ServiceCollection();
        collection.AddSingleton<ILog>(_log);
        collection.AddSingleton<ThemeManager>();
        collection.AddSingleton<UpdateService>();
        collection.AddSingleton<ShellViewModel>();
        collection.AddSingleton<MainWindow>();
        collection.AddTransient<DashboardPage>();
        collection.AddTransient<ConflictsPage>();
        collection.AddTransient<DriversPage>();
        collection.AddTransient<MonitorPage>();
        collection.AddTransient<UninstallPage>();
        collection.AddTransient<InventoryPage>();
        collection.AddTransient<LogPage>();
        collection.AddTransient<AppearancePage>();
        collection.AddTransient<UpdatesPage>();
        _services = collection.BuildServiceProvider();

        // Le theme est applique avant la premiere fenetre : sinon elle apparait
        // brievement dans les couleurs par defaut avant de basculer.
        _services.GetRequiredService<ThemeManager>().LoadSaved();

        ReportPendingTransactions();

        base.OnStartup(e);
        _services.GetRequiredService<MainWindow>().Show();
        Updates.StartBackgroundChecks();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _log?.Write(LogLevel.Info, $"arret, {_guard?.HandledCount ?? 0} exception(s) interceptee(s)");
        _services?.Dispose();
        _log?.Dispose();
        base.OnExit(e);
    }

    /// <summary>
    /// Signale les transactions restees ouvertes, signe d'une interruption lors d'une
    /// session precedente. La restauration effective viendra avec les remediations du
    /// lot 5 : aucun applicateur ne touche encore au systeme, il n'y a donc rien a
    /// defaire, mais le journal doit deja etre relu et trace.
    /// </summary>
    private void ReportPendingTransactions()
    {
        try
        {
            var pending = new FileTransactionStore(AppPaths.JournalDirectory).ReadPending();

            if (pending.Count == 0) return;

            _log!.Write(LogLevel.Warn,
                $"{pending.Count} transaction(s) interrompue(s) en attente de restauration");

            foreach (var transaction in pending)
            {
                _log.Write(LogLevel.Warn,
                    $"  {transaction.Id:N} {transaction.Title} (affichage : {transaction.AffectsDisplay})");
            }
        }
        catch (Exception ex)
        {
            // Un journal illisible ne doit pas empecher l'application de demarrer :
            // c'est precisement dans cette situation qu'elle est le plus utile.
            _log!.Write(LogLevel.Error, "lecture du journal transactionnel impossible", ex);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e) =>
        e.Handled = _guard!.HandleUi(e.Exception);

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e) =>
        _guard!.HandleDomain(e.ExceptionObject, e.IsTerminating);

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        if (_guard!.HandleUnobservedTask(e.Exception)) e.SetObserved();
    }
}
