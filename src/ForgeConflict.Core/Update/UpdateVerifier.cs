using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace ForgeConflict.Core.Update;

/// <summary>Echec d'une etape de mise a jour ; le message est destine a l'utilisateur.</summary>
public sealed class UpdateException : Exception
{
    public UpdateException(string message) : base(message) { }
    public UpdateException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>Mise a jour annoncee par un manifeste dont la signature est valide.</summary>
public sealed record UpdateInfo(string Version, string Notes, string Url, long Size, string Sha256);

/// <summary>
/// Partie pure et testable : signature du manifeste, contraintes sur son contenu,
/// comparaison de versions. Aucune entree/sortie reseau ou disque ici.
/// </summary>
public static class UpdateVerifier
{
    /// <summary>Cle publique ECDSA P-256 (X||Y en base64). La privee est dans D:\MDP, hors depot.</summary>
    public const string PublicKey =
        "E8nAPo5zS3ev8ZcoHGFx78ibtqU+W14pcPn6Brou0ibptMBZTfss2zWwH9Py/t8HQWkuexMKe/22LzgRjCADIw==";

    public const string ManifestUrl =
        "https://github.com/Heiphaistos/ForgeConflict/releases/latest/download/latest.json";

    /// <summary>Seul prefixe autorise pour l'exe a telecharger.</summary>
    public const string ChannelPrefix =
        "https://github.com/Heiphaistos/ForgeConflict/releases/download/";

    public const long MaxExeBytes = 300L * 1024 * 1024;

    public static UpdateInfo Parse(byte[] manifest, byte[] signatureBase64, string publicKey = PublicKey)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(signatureBase64);

        if (!VerifySignature(manifest, signatureBase64, publicKey))
            throw new UpdateException("La signature du manifeste de mise à jour est invalide.");

        UpdateInfo info;
        try
        {
            using var doc = JsonDocument.Parse(manifest);
            var root = doc.RootElement;
            info = new UpdateInfo(
                root.GetProperty("version").GetString() ?? "",
                root.TryGetProperty("notes", out var n) ? n.GetString() ?? "" : "",
                root.GetProperty("url").GetString() ?? "",
                root.GetProperty("size").GetInt64(),
                (root.GetProperty("sha256").GetString() ?? "").ToLowerInvariant());
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new UpdateException("Le manifeste de mise à jour est illisible.", ex);
        }

        if (!TryParseVersion(info.Version, out _))
            throw new UpdateException("Numéro de version invalide dans le manifeste.");
        if (!IsAllowedUrl(info.Url))
            throw new UpdateException("L'adresse de téléchargement est hors du canal officiel.");
        if (info.Size is <= 0 or > MaxExeBytes)
            throw new UpdateException("Taille annoncée invalide.");
        if (info.Sha256.Length != 64 || !info.Sha256.All(Uri.IsHexDigit))
            throw new UpdateException("Empreinte SHA-256 invalide dans le manifeste.");
        return info;
    }

    public static bool VerifySignature(byte[] data, byte[] signatureBase64, string publicKey)
    {
        try
        {
            var xy = Convert.FromBase64String(publicKey.Trim());
            var sig = Convert.FromBase64String(System.Text.Encoding.ASCII.GetString(signatureBase64).Trim());
            using var ecdsa = ECDsa.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = xy[..32], Y = xy[32..] },
            });
            return ecdsa.VerifyData(data, sig, HashAlgorithmName.SHA256);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or ArgumentException)
        {
            return false;
        }
    }

    public static bool IsAllowedUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        return uri.Scheme == Uri.UriSchemeHttps
            && uri.UserInfo.Length == 0
            && uri.Query.Length == 0
            && uri.Fragment.Length == 0
            && uri.IsDefaultPort
            && uri.AbsoluteUri.StartsWith(ChannelPrefix, StringComparison.Ordinal);
    }

    public static bool TryParseVersion(string text, out Version version)
    {
        var t = text.Trim().TrimStart('v');
        var cut = t.IndexOfAny(['-', '+']);
        if (cut >= 0) t = t[..cut];
        return Version.TryParse(t, out version!) && version.Major >= 0;
    }

    public static bool IsNewer(string current, string candidate) =>
        TryParseVersion(current, out var c) && TryParseVersion(candidate, out var n) && n > c;

    public static string Hex(byte[] hash) => Convert.ToHexString(hash).ToLower(CultureInfo.InvariantCulture);
}
