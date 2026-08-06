using System.Diagnostics;
using System.Text.RegularExpressions;
using ForgeConflict.Core.Inventory.Model;
using Microsoft.Win32;

namespace ForgeConflict.Core.Inventory.Collectors;

/// <summary>
/// Releve le plan d'alimentation actif et l'etat de l'economie d'energie du lien PCIe.
/// Une gestion agressive du lien peut brider un GPU discret.
/// </summary>
public sealed partial class PowerCollector : ICollector<PowerProfile>
{
    /// <summary>Sous-groupe PCI Express, puis parametre ASPM du plan d'alimentation.</summary>
    private const string PowerSettingsKey =
        @"SYSTEM\CurrentControlSet\Control\Power\PowerSettings\501a4d13-42af-4429-9fd1-a8218c268e20\ee12f906-d277-404b-b6da-e5fa1a576df5";

    public string Source => "alimentation";

    public TimeSpan Timeout => TimeSpan.FromSeconds(20);

    public PowerProfile Collect(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var (guid, name) = ReadActiveScheme();

        using var key = Registry.LocalMachine.OpenSubKey(PowerSettingsKey, writable: false);

        // Defaut Windows : economie d'energie maximale sur le lien PCIe.
        return new PowerProfile(
            ActiveSchemeGuid: guid,
            ActiveSchemeName: name,
            PciExpressLinkStateOnAc: Read(key, "ACSettingIndex", 2),
            PciExpressLinkStateOnBattery: Read(key, "DCSettingIndex", 2));
    }

    private static RegistrySetting Read(RegistryKey? key, string name, object effectiveDefault)
    {
        var value = key?.GetValue(name);
        return value is null
            ? RegistrySetting.Absent(name, effectiveDefault)
            : RegistrySetting.Present(name, value, effectiveDefault);
    }

    /// <summary>
    /// powercfg rend une ligne du type "GUID du mode ... : &lt;guid&gt;  (Nom)".
    /// Le libelle est traduit, seuls le GUID et la parenthese sont extraits.
    /// </summary>
    private static (string? Guid, string? Name) ReadActiveScheme()
    {
        try
        {
            var info = new ProcessStartInfo
            {
                FileName = "powercfg.exe",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            info.ArgumentList.Add("/getactivescheme");

            using var process = Process.Start(info);
            if (process is null) return (null, null);

            var output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(10_000))
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                return (null, null);
            }

            var guid = GuidPattern().Match(output);
            var name = NamePattern().Match(output);

            return (guid.Success ? guid.Value : null,
                    name.Success ? name.Groups[1].Value.Trim() : null);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return (null, null);
        }
    }

    [GeneratedRegex(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex GuidPattern();

    [GeneratedRegex(@"\(([^)]+)\)")]
    private static partial Regex NamePattern();
}
