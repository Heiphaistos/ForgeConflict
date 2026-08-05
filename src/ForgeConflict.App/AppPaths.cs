// System.IO ne fait pas partie des usings implicites d'un projet WPF.
using System.IO;

namespace ForgeConflict.App;

/// <summary>
/// Emplacements de travail. Les journaux vivent a cote de l'executable pour rester
/// portables ; les donnees qui doivent survivre a un deplacement de l'exe iront
/// dans ProgramData a partir du lot dedie a la couche de surete.
/// </summary>
public static class AppPaths
{
    public static string BaseDirectory => AppContext.BaseDirectory;

    public static string LogDirectory => Path.Combine(BaseDirectory, ".logs");

    public const long LogRotationBytes = 1_048_576;
}
