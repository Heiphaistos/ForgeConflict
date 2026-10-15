using System.Text.Json;

namespace ForgeConflict.Core.Update;

/// <summary>
/// Reglage « verifier automatiquement », dans un petit fichier a cote de l'exe
/// (le fichier de themes existant est ecrase en entier a chaque sauvegarde : on
/// ne le partage pas). Actif par defaut ; un support en lecture seule garde le choix
/// pour la session seulement.
/// </summary>
public sealed class UpdateSettings
{
    private readonly string _path;

    public UpdateSettings(string path)
    {
        _path = path;
        try
        {
            if (File.Exists(_path))
                AutoCheck = JsonSerializer.Deserialize<Data>(File.ReadAllText(_path))?.AutoCheck ?? true;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            AutoCheck = true;
        }
    }

    public bool AutoCheck { get; private set; } = true;

    /// <summary>Renvoie false si le choix n'a pas pu etre ecrit sur disque.</summary>
    public bool SetAutoCheck(bool value)
    {
        AutoCheck = value;
        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(new Data(value)));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private sealed record Data(bool AutoCheck);
}
