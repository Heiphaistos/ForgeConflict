using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using ForgeConflict.Core.Logging;

namespace ForgeConflict.Core.Update;

/// <summary>
/// Cycle complet : lire le manifeste signe, telecharger l'exe en « .new » en verifiant
/// taille et SHA-256, controler le signataire Authenticode, puis confier le remplacement
/// a un .cmd detache qui attend la fin du process, remplace l'exe et le relance.
/// </summary>
public sealed class AppUpdater
{
    private const int MaxManifestBytes = 64 * 1024;
    private const int MaxSignatureBytes = 1024;

    private readonly HttpClient _http;
    private readonly ILog _log;
    private readonly string _manifestUrl;
    private readonly string _publicKey;

    public AppUpdater(HttpClient http, ILog log, string manifestUrl = UpdateVerifier.ManifestUrl,
        string publicKey = UpdateVerifier.PublicKey)
    {
        _http = http;
        _log = log;
        _manifestUrl = manifestUrl;
        _publicKey = publicKey;
    }

    /// <summary>Renvoie la mise a jour disponible, ou null si <paramref name="currentVersion"/> est a jour.</summary>
    public async Task<UpdateInfo?> CheckAsync(string currentVersion, CancellationToken ct = default)
    {
        try
        {
            var manifest = await GetBytesAsync(_manifestUrl, MaxManifestBytes, ct);
            var signature = await GetBytesAsync(_manifestUrl + ".sig", MaxSignatureBytes, ct);
            var info = UpdateVerifier.Parse(manifest, signature, _publicKey);
            return UpdateVerifier.IsNewer(currentVersion, info.Version) ? info : null;
        }
        catch (HttpRequestException ex)
        {
            throw new UpdateException("Impossible de joindre le serveur de mises à jour : " + ex.Message, ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new UpdateException("Le serveur de mises à jour ne répond pas (délai dépassé).", ex);
        }
    }

    /// <summary>Telecharge dans «exe».new, verifie taille et SHA-256 puis le signataire. Efface le fichier si un controle echoue.</summary>
    public async Task<string> DownloadAsync(UpdateInfo info, string exePath, IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        if (!UpdateVerifier.IsAllowedUrl(info.Url))
            throw new UpdateException("L'adresse de téléchargement est hors du canal officiel.");

        var target = exePath + ".new";
        try
        {
            await using (var file = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await DownloadToAsync(info, file, progress, ct);
            }

            var hash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(target, ct)));
            if (!string.Equals(hash, info.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new UpdateException("Le fichier téléchargé est corrompu (empreinte SHA-256 différente).");

            var refusal = await AuthenticodeGuard.CheckAsync(exePath, target, m => _log.Write(LogLevel.Info, m));
            if (refusal is not null) throw new UpdateException(refusal);
            return target;
        }
        catch (Exception ex) when (ex is UpdateException or HttpRequestException or IOException
            or UnauthorizedAccessException or OperationCanceledException)
        {
            TryDelete(target);
            if (ex is UpdateException or OperationCanceledException) throw;
            throw new UpdateException("Téléchargement impossible : " + ex.Message, ex);
        }
    }

    /// <summary>Telecharge dans <paramref name="destination"/> en verifiant la taille exacte annoncee.</summary>
    public async Task DownloadToAsync(UpdateInfo info, Stream destination, IProgress<double>? progress,
        CancellationToken ct)
    {
        using var response = await _http.GetAsync(info.Url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await using var source = await response.Content.ReadAsStreamAsync(ct);
        var buffer = new byte[81920];
        long done = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            done += read;
            if (done > info.Size) throw new UpdateException("Le fichier téléchargé dépasse la taille annoncée.");
            await destination.WriteAsync(buffer.AsMemory(0, read), ct);
            progress?.Report((double)done / info.Size);
        }
        if (done != info.Size) throw new UpdateException("Le fichier téléchargé est incomplet (taille différente).");
    }

    /// <summary>Script de remplacement : reessaie jusqu'a ce que l'exe soit libere, relance, puis s'efface.</summary>
    public static string BuildScript(string newPath, string exePath)
    {
        static string Q(string p) => p.Replace("%", "%%");
        var dir = Path.GetDirectoryName(exePath) ?? ".";
        return string.Join("\r\n",
            "@echo off",
            "chcp 65001 >nul",
            "set \"N=0\"",
            ":retry",
            $"move /y \"{Q(newPath)}\" \"{Q(exePath)}\" >nul 2>&1",
            "if not errorlevel 1 goto done",
            "set /a N+=1",
            "if %N% GEQ 90 goto fin",
            "ping -n 2 127.0.0.1 >nul",
            "goto retry",
            ":done",
            $"start \"\" /d \"{Q(dir)}\" \"{Q(exePath)}\"",
            ":fin",
            "(goto) 2>nul & del \"%~f0\"",
            "");
    }

    /// <summary>Ecrit le .cmd et le lance detache. L'appelant doit quitter aussitot apres.</summary>
    public static void LaunchReplacement(string newPath, string exePath)
    {
        var script = Path.Combine(Path.GetTempPath(), $"forgeconflict-maj-{Guid.NewGuid():N}.cmd");
        File.WriteAllText(script, BuildScript(newPath, exePath), new UTF8Encoding(false));
        Process.Start(new ProcessStartInfo("cmd.exe", $"/d /c \"\"{script}\"\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        })?.Dispose();
    }

    private async Task<byte[]> GetBytesAsync(string url, int limit, CancellationToken ct)
    {
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var ms = new MemoryStream();
        var buffer = new byte[4096];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            ms.Write(buffer, 0, read);
            if (ms.Length > limit) throw new UpdateException("Réponse du serveur de mises à jour trop volumineuse.");
        }
        return ms.ToArray();
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* meilleur effort */ }
    }
}
