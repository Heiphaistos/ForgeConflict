using System.Diagnostics;
using System.Text;
using ForgeConflict.Core.Logging;
using ForgeConflict.Core.Safety;

namespace ForgeConflict.Core.Remediation;

/// <summary>
/// Retire un paquet du magasin de pilotes.
///
/// Cette operation n'est PAS reversible au sens strict : pnputil supprime le paquet
/// du magasin, et seul le fichier d'installation d'origine permettrait de le
/// remettre. Le retour arriere se limite donc a signaler ce qui a ete retire.
/// C'est pourquoi cette remediation exige un point de restauration systeme, seul
/// mecanisme capable de rendre reellement l'etat anterieur.
/// </summary>
public sealed class DriverStoreChangeApplier : IChangeApplier
{
    private readonly ILog _log;

    public DriverStoreChangeApplier(ILog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        _log = log;
    }

    public void Apply(ChangeRecord change)
    {
        ArgumentNullException.ThrowIfNull(change);

        if (change.Kind != ChangeKind.ScheduledTask)
        {
            throw new NotSupportedException($"Type de modification non gere ici : {change.Kind}.");
        }

        var inf = change.Target;
        var (code, output) = Run("/delete-driver", inf, "/uninstall");

        if (code != 0)
        {
            throw new InvalidOperationException(
                $"Le paquet {inf} n'a pas pu etre retire (code {code}). {FirstLine(output)}");
        }

        _log.Write(LogLevel.Info, $"paquet retire du magasin : {inf}");
    }

    public void Revert(ChangeRecord change)
    {
        ArgumentNullException.ThrowIfNull(change);

        // Le paquet supprime ne peut pas etre recree a partir de rien. Le signaler
        // franchement vaut mieux qu'un silence qui laisserait croire a une
        // restauration effective.
        _log.Write(LogLevel.Warn,
            $"le paquet {change.Target} ne peut pas etre remis en place automatiquement : " +
            "utilisez le point de restauration systeme cree avant l'operation, ou reinstallez " +
            "le pilote depuis le site du constructeur.");
    }

    private static (int Code, string Output) Run(params string[] arguments)
    {
        var info = new ProcessStartInfo
        {
            FileName = "pnputil.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
        };

        foreach (var argument in arguments) info.ArgumentList.Add(argument);

        using var process = Process.Start(info)
            ?? throw new InvalidOperationException("pnputil n'a pas pu etre lance.");

        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();

        if (!process.WaitForExit(120_000))
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw new TimeoutException("pnputil n'a pas rendu la main dans le delai imparti.");
        }

        return (process.ExitCode, output);
    }

    private static string FirstLine(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? string.Empty;
}
