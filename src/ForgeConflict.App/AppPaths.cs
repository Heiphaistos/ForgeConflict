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

    /// <summary>
    /// Le journal transactionnel vit dans ProgramData et non a cote de l'executable :
    /// il doit survivre au deplacement, au renommage ou a la suppression de l'exe
    /// portable, sans quoi une machine modifiee perdrait son moyen de retour.
    /// </summary>
    public static string JournalDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "ForgeConflict", "journal");

    public const long LogRotationBytes = 1_048_576;
}
