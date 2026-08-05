namespace ForgeConflict.Core.Logging;

/// <summary>
/// Journal de l'application. Une implementation ne doit jamais lever d'exception :
/// un echec d'ecriture du journal ne peut pas devenir une cause de plantage.
/// </summary>
public interface ILog
{
    void Write(LogLevel level, string message, Exception? exception = null);
}
