using System.Diagnostics;
using System.Text;

namespace ForgeConflict.Core.Update;

/// <summary>
/// Defense en profondeur : si l'exe courant est signe Authenticode, le nouveau doit
/// l'etre par le MEME certificat (empreinte identique) et son contenu ne doit pas
/// avoir ete altere apres signature. Le certificat Heiphaistos est auto-signe : on
/// ne demande donc PAS une chaine de confiance, seulement l'identite du signataire.
/// </summary>
public static class AuthenticodeGuard
{
    public sealed record Signature(string Status, string Thumbprint, string Subject);

    private const string Script =
        "$s = Get-AuthenticodeSignature -LiteralPath $env:FC_PATH; " +
        "$c = $s.SignerCertificate; " +
        "if ($c) { \"$($s.Status)|$($c.Thumbprint)|$($c.Subject)\" } else { \"$($s.Status)||\" }";

    /// <summary>Renvoie null si accepte (ou non applicable, trace dans <paramref name="log"/>), sinon le motif de refus.</summary>
    public static async Task<string?> CheckAsync(string currentExe, string newExe, Action<string> log)
    {
        var current = await ReadAsync(currentExe);
        if (current is null)
        {
            log("signature de l'exe courant illisible : controle Authenticode ignore");
            return null;
        }
        if (current.Thumbprint.Length == 0)
        {
            log("exe courant non signe : controle Authenticode ignore");
            return null;
        }

        return Decide(current, await ReadAsync(newExe));
    }

    public static string? Decide(Signature current, Signature? fresh)
    {
        if (fresh is null) return "Impossible de lire la signature Authenticode du nouvel exécutable.";
        if (fresh.Thumbprint.Length == 0) return "Le nouvel exécutable n'est pas signé alors que l'actuel l'est.";
        if (!string.Equals(fresh.Thumbprint, current.Thumbprint, StringComparison.OrdinalIgnoreCase))
            return $"Le nouvel exécutable est signé par un autre certificat ({fresh.Subject}).";
        if (fresh.Status is "HashMismatch" or "NotSigned")
            return "La signature du nouvel exécutable ne correspond plus à son contenu.";
        return null;
    }

    public static Signature? Parse(string line)
    {
        var parts = line.Trim().Split('|', 3);
        return parts.Length == 3 ? new Signature(parts[0], parts[1], parts[2]) : null;
    }

    private static async Task<Signature?> ReadAsync(string path)
    {
        try
        {
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(Script));
            var psi = new ProcessStartInfo("powershell.exe",
                $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
            };
            psi.Environment["FC_PATH"] = path;
            using var process = Process.Start(psi);
            if (process is null) return null;
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            return process.ExitCode == 0 ? Parse(output) : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }
    }
}
