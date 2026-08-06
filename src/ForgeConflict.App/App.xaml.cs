using System.Windows;
using System.Windows.Threading;
using ForgeConflict.App.Theme;
using ForgeConflict.App.ViewModels;
using ForgeConflict.App.Views;
using ForgeConflict.Core.Diagnostics;
using ForgeConflict.Core.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace ForgeConflict.App;

public partial class App : Application
{
    private FileLog? _log;
    private ExceptionGuard? _guard;
    private ServiceProvider? _services;

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
        collection.AddSingleton<ShellViewModel>();
        collection.AddSingleton<MainWindow>();
        collection.AddTransient<DashboardPage>();
        collection.AddTransient<InventoryPage>();
        collection.AddTransient<LogPage>();
        collection.AddTransient<AppearancePage>();
        _services = collection.BuildServiceProvider();

        // Le theme est applique avant la premiere fenetre : sinon elle apparait
        // brievement dans les couleurs par defaut avant de basculer.
        _services.GetRequiredService<ThemeManager>().LoadSaved();

        base.OnStartup(e);
        _services.GetRequiredService<MainWindow>().Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _log?.Write(LogLevel.Info, $"arret, {_guard?.HandledCount ?? 0} exception(s) interceptee(s)");
        _services?.Dispose();
        _log?.Dispose();
        base.OnExit(e);
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
