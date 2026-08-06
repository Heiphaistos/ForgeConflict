using ForgeConflict.Core.Logging;
using Microsoft.Win32;

namespace ForgeConflict.Core.Safety;

/// <summary>
/// Applique et defait une modification du registre.
///
/// Le point capital est le traitement de l'absence : restaurer une valeur qui
/// n'existait pas la SUPPRIME, pour que Windows reapplique son propre defaut.
/// Y ecrire zero imposerait une valeur que la machine n'a jamais eue, ce qui est
/// une modification deguisee en restauration.
/// </summary>
public sealed class RegistryChangeApplier : IChangeApplier
{
    private readonly ILog _log;

    public RegistryChangeApplier(ILog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        _log = log;
    }

    public void Apply(ChangeRecord change)
    {
        ArgumentNullException.ThrowIfNull(change);

        if (change.Kind != ChangeKind.RegistryValue)
        {
            throw new NotSupportedException(
                $"Ce type de modification n'est pas gere ici : {change.Kind}.");
        }

        using var key = OpenForWrite(change.Target);

        if (change.NewValue is null)
        {
            key.DeleteValue(change.ValueName!, throwOnMissingValue: false);
            _log.Write(LogLevel.Info, $"valeur supprimee : {change.Target}\\{change.ValueName}");
            return;
        }

        key.SetValue(change.ValueName!, change.NewValue, RegistryValueKind.DWord);
        _log.Write(LogLevel.Info, $"valeur ecrite : {change.Describe()}");
    }

    public void Revert(ChangeRecord change)
    {
        ArgumentNullException.ThrowIfNull(change);

        if (change.Kind != ChangeKind.RegistryValue)
        {
            throw new NotSupportedException(
                $"Ce type de modification n'est pas gere ici : {change.Kind}.");
        }

        using var key = OpenForWrite(change.Target);

        if (!change.PreviousValueExisted)
        {
            // La valeur n'existait pas avant : la restauration consiste a la retirer,
            // pas a y ecrire zero. Windows retrouve alors son comportement par defaut.
            key.DeleteValue(change.ValueName!, throwOnMissingValue: false);
            _log.Write(LogLevel.Info,
                $"restauration : {change.Target}\\{change.ValueName} supprimee (elle n'existait pas)");
            return;
        }

        key.SetValue(change.ValueName!, change.PreviousValue!, RegistryValueKind.DWord);
        _log.Write(LogLevel.Info,
            $"restauration : {change.Target}\\{change.ValueName} remise a {change.PreviousValue}");
    }

    /// <summary>
    /// Ouvre la cle en ecriture, en la creant si besoin. Le chemin porte sa ruche en
    /// prefixe, ex. « HKLM\SYSTEM\... ».
    /// </summary>
    private static RegistryKey OpenForWrite(string path)
    {
        var (hive, subPath) = SplitHive(path);

        return hive.CreateSubKey(subPath, writable: true)
               ?? throw new InvalidOperationException($"Cle registre inaccessible : {path}");
    }

    private static (RegistryKey Hive, string SubPath) SplitHive(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var separator = path.IndexOf('\\', StringComparison.Ordinal);
        if (separator <= 0)
        {
            throw new ArgumentException($"Chemin de registre incomplet : {path}", nameof(path));
        }

        var prefix = path[..separator];
        var subPath = path[(separator + 1)..];

        RegistryKey hive = prefix.ToUpperInvariant() switch
        {
            "HKLM" or "HKEY_LOCAL_MACHINE" => Registry.LocalMachine,
            "HKCU" or "HKEY_CURRENT_USER" => Registry.CurrentUser,
            _ => throw new ArgumentException($"Ruche de registre inconnue : {prefix}", nameof(path)),
        };

        return (hive, subPath);
    }
}
