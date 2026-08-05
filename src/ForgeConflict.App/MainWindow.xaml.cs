using System.IO;
using System.Security.Principal;
using System.Text;
using System.Windows;
using ForgeConflict.Core.Collection;
using ForgeConflict.Core.Inventory;
using ForgeConflict.Core.Logging;

namespace ForgeConflict.App;

public partial class MainWindow : Window
{
    private readonly ILog _log = ((App)Application.Current).Log;
    private SystemSnapshot? _lastSnapshot;

    public MainWindow() => InitializeComponent();

    private async void OnScan(object sender, RoutedEventArgs e)
    {
        Scan.IsEnabled = false;
        Output.Text = "Analyse en cours...";
        try
        {
            var scanner = new InventoryScanner(new IsolatedRunner(_log));
            _lastSnapshot = await scanner.ScanAsync();
            Output.Text = SnapshotReport.Render(_lastSnapshot);
            _log.Write(LogLevel.Info,
                $"analyse terminee, {_lastSnapshot.UnusableSources.Count} source(s) indisponible(s)");
        }
        finally
        {
            Scan.IsEnabled = true;
        }
    }

    private void OnExport(object sender, RoutedEventArgs e)
    {
        if (_lastSnapshot is null)
        {
            Output.Text = "Lancez d'abord une analyse.";
            return;
        }

        var directory = Path.Combine(AppPaths.BaseDirectory, "snapshots");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"snapshot-{DateTime.Now:yyyyMMdd-HHmmss}.json");

        File.WriteAllText(
            path,
            SnapshotSerializer.Serialize(SnapshotReport.ToEnvelope(_lastSnapshot, ThisAssembly.Version)),
            Encoding.UTF8);

        Output.Text = $"Instantane ecrit dans :\r\n{path}";
        _log.Write(LogLevel.Info, $"instantane exporte : {path}");
    }

    private async void OnSelfTest(object sender, RoutedEventArgs e)
    {
        var runner = new IsolatedRunner(_log);
        var report = new StringBuilder();

        report.AppendLine($"Version        : {ThisAssembly.Version}");
        report.AppendLine($"Administrateur : {IsElevated()}");
        report.AppendLine($"Journal        : {AppPaths.LogDirectory}");
        report.AppendLine();

        var ok = await runner.RunAsync("succes", _ => "valeur collectee", TimeSpan.FromSeconds(2));
        var failed = await runner.RunAsync<string>(
            "echec", _ => throw new UnauthorizedAccessException("acces refuse"), TimeSpan.FromSeconds(2));
        var timedOut = await runner.RunAsync<string>(
            "blocage", _ => { Thread.Sleep(TimeSpan.FromSeconds(10)); return "jamais"; }, TimeSpan.FromMilliseconds(300));

        foreach (var fragment in new[] { ok, failed, timedOut })
        {
            report.AppendLine(
                $"{fragment.Source,-10} {fragment.Status,-10} {fragment.Duration.TotalMilliseconds,7:0} ms  {fragment.Error ?? fragment.Value ?? "-"}");
        }

        report.AppendLine();
        report.AppendLine("Le blocage doit rendre la main en ~300 ms, pas en 10 s.");
        Output.Text = report.ToString();
        _log.Write(LogLevel.Info, "verification du socle terminee");
    }

    private void OnProvoke(object sender, RoutedEventArgs e) =>
        throw new InvalidOperationException("exception volontaire de verification");

    private static string IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator) ? "oui" : "non";
    }
}
