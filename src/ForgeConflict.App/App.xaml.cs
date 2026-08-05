using System.Windows;
using System.Windows.Threading;
using ForgeConflict.Core.Diagnostics;
using ForgeConflict.Core.Logging;

namespace ForgeConflict.App;

public partial class App : Application
{
    private FileLog? _log;
    private ExceptionGuard? _guard;

    internal ILog Log => _log ?? throw new InvalidOperationException("Journal non initialise.");

    protected override void OnStartup(StartupEventArgs e)
    {
        _log = new FileLog(AppPaths.LogDirectory, AppPaths.LogRotationBytes, () => DateTimeOffset.Now);
        _guard = new ExceptionGuard(_log);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        _log.Write(LogLevel.Info, $"demarrage de ForgeConflict {ThisAssembly.Version}");
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _log?.Write(LogLevel.Info, $"arret, {_guard?.HandledCount ?? 0} exception(s) interceptee(s)");
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
