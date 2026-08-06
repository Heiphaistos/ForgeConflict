# ForgeConflict — Lot 1C : inventaire natif

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Compléter l'inventaire avec les cinq sources restantes du spec section 4 — historique de plantage, dumps noyau, configuration PCIe, alimentation, écrans — en isolant tous les appels natifs dans `ForgeConflict.Native`.

**Architecture:** `ForgeConflict.Native` reçoit ses premières façades P/Invoke (`cfgmgr32.dll`), derrière une interface typée. Les cinq collecteurs suivent le contrat `ICollector<T>` du lot 1B et rejoignent `InventoryScanner`, qui passe de cinq à dix sources.

**Tech Stack:** .NET 8, `System.Diagnostics.Eventing.Reader`, `cfgmgr32.dll` en P/Invoke, `powercfg`, WMI.

**Référence:** `docs/superpowers/specs/2026-08-05-forgeconflict-design.md` section 4.

**Version :** 0.2.0 → 0.3.0

---

## Décisions issues de la reconnaissance du 06/08/2026

**Un identifiant d'événement n'est unique que par fournisseur.** Filtrer le journal System sur les
seuls IDs 4101, 14, 18, 19 a ramené `WindowsUpdateClient` id 19, `BTHUSB` id 18 et `TPM` id 18 —
aucun rapport avec le GPU. Tout filtre doit donc porter le couple fournisseur + identifiant, jamais
l'identifiant seul. C'est la différence entre un diagnostic et du bruit.

**L'absence de plantage est le cas normal.** La machine de référence n'a ni `LiveKernelReports`, ni
minidump, ni le moindre événement `nvlddmkm`. Les collecteurs doivent rendre une liste vide sans
échouer, et les tests ne peuvent pas exiger la présence d'un plantage — sinon ils échoueraient sur
toute machine saine, à commencer par la VM.

**Les propriétés PCIe passent par `cfgmgr32`.** `CM_Get_DevNode_PropertyW` expose
`CurrentLinkWidth`, `MaxLinkWidth`, `CurrentLinkSpeed` et `MaxLinkSpeed`. Aucune API managée ne les
donne. Constat sur la machine de référence : le RTX 3070 est en **x8 pour un maximum de x16** —
exactement la dégradation à détecter.

**Les fréquences d'écran diffèrent entre adaptateurs.** 1920×1080 à 144 Hz sur l'Intel,
2560×1440 à 143 Hz sur le NVIDIA. Une configuration hybride mêle des modes hétérogènes, ce qui est
normal en Optimus et ne doit pas être signalé comme une anomalie à ce stade.

---

### Task 1 : Façade P/Invoke des propriétés de périphérique

**Files:**
- Create: `src/ForgeConflict.Native/DeviceProperty.cs`
- Create: `src/ForgeConflict.Native/IDevicePropertyReader.cs`
- Create: `src/ForgeConflict.Native/CfgMgrDevicePropertyReader.cs`
- Test: `tests/ForgeConflict.Tests/Native/DevicePropertyReaderTests.cs`
- Modify: `tests/ForgeConflict.Tests/ForgeConflict.Tests.csproj`

- [ ] **Step 1 : Référencer Native depuis les tests**

Modify `tests/ForgeConflict.Tests/ForgeConflict.Tests.csproj` — ajouter dans le dernier `ItemGroup` :

```xml
    <ProjectReference Include="..\..\src\ForgeConflict.Native\ForgeConflict.Native.csproj" />
```

- [ ] **Step 2 : Écrire les tests qui échouent**

Create `tests/ForgeConflict.Tests/Native/DevicePropertyReaderTests.cs` :

```csharp
using ForgeConflict.Native;

namespace ForgeConflict.Tests.Native;

public sealed class DevicePropertyReaderTests
{
    private static string? FirstDisplayInstanceId()
    {
        using var searcher = new System.Management.ManagementObjectSearcher(
            "SELECT PNPDeviceID FROM Win32_VideoController");
        using var rows = searcher.Get();

        foreach (var row in rows)
        {
            using (row)
            {
                var id = row["PNPDeviceID"]?.ToString();
                if (!string.IsNullOrWhiteSpace(id) &&
                    id.StartsWith("PCI", StringComparison.OrdinalIgnoreCase))
                {
                    return id;
                }
            }
        }

        return null;
    }

    [Fact]
    public void TryReadUInt32_ReadsPcieLinkWidthOfARealAdapter()
    {
        var instanceId = FirstDisplayInstanceId();
        if (instanceId is null) return; // machine sans GPU PCI, ex. certaines VM

        var reader = new CfgMgrDevicePropertyReader();

        Assert.True(reader.TryReadUInt32(instanceId, DeviceProperty.PciCurrentLinkWidth, out var current));
        Assert.True(reader.TryReadUInt32(instanceId, DeviceProperty.PciMaxLinkWidth, out var max));

        // Une largeur de lien PCIe est une puissance de deux entre 1 et 32.
        Assert.InRange(current, 1u, 32u);
        Assert.InRange(max, 1u, 32u);
        Assert.True(current <= max, $"largeur courante {current} superieure au maximum {max}");
    }

    [Fact]
    public void TryReadUInt32_ReturnsFalseForAnUnknownDeviceInsteadOfThrowing()
    {
        var reader = new CfgMgrDevicePropertyReader();

        Assert.False(reader.TryReadUInt32(
            @"PCI\VEN_FFFF&DEV_FFFF\inexistant", DeviceProperty.PciCurrentLinkWidth, out var value));
        Assert.Equal(0u, value);
    }

    [Fact]
    public void TryReadUInt32_ReturnsFalseForNullOrEmptyInstanceId()
    {
        var reader = new CfgMgrDevicePropertyReader();

        Assert.False(reader.TryReadUInt32(null, DeviceProperty.PciMaxLinkSpeed, out _));
        Assert.False(reader.TryReadUInt32("", DeviceProperty.PciMaxLinkSpeed, out _));
    }

    [Fact]
    public void TryReadString_ReadsTheLocationOfARealAdapter()
    {
        var instanceId = FirstDisplayInstanceId();
        if (instanceId is null) return;

        var reader = new CfgMgrDevicePropertyReader();

        Assert.True(reader.TryReadString(instanceId, DeviceProperty.DeviceLocationInfo, out var location));
        Assert.False(string.IsNullOrWhiteSpace(location));
    }
}
```

- [ ] **Step 3 : Lancer les tests pour vérifier l'échec**

Run : `dotnet test ForgeConflict.sln --filter FullyQualifiedName~DevicePropertyReaderTests`
Expected : échec de compilation, `CfgMgrDevicePropertyReader` étant introuvable.

- [ ] **Step 4 : Écrire les identifiants de propriété**

Create `src/ForgeConflict.Native/DeviceProperty.cs` :

```csharp
namespace ForgeConflict.Native;

/// <summary>
/// Cle de propriete de peripherique (DEVPROPKEY). Le couple GUID + identifiant est
/// fixe par Windows : ces valeurs sont documentees et ne doivent jamais etre devinees.
/// </summary>
public readonly record struct DeviceProperty(Guid Category, uint PropertyId)
{
    // GUID des proprietes PCI : {3ab22e31-8264-4b4e-9af5-a8d2d8e33e62}
    private static readonly Guid PciCategory = new("3ab22e31-8264-4b4e-9af5-a8d2d8e33e62");

    // GUID des proprietes generales de peripherique : {a45c254e-df1c-4efd-8020-67d146a850e0}
    private static readonly Guid DeviceCategory = new("a45c254e-df1c-4efd-8020-67d146a850e0");

    public static DeviceProperty PciCurrentLinkSpeed => new(PciCategory, 9);
    public static DeviceProperty PciCurrentLinkWidth => new(PciCategory, 10);
    public static DeviceProperty PciMaxLinkSpeed => new(PciCategory, 11);
    public static DeviceProperty PciMaxLinkWidth => new(PciCategory, 12);
    public static DeviceProperty DeviceLocationInfo => new(DeviceCategory, 15);
}
```

- [ ] **Step 5 : Écrire le contrat**

Create `src/ForgeConflict.Native/IDevicePropertyReader.cs` :

```csharp
namespace ForgeConflict.Native;

/// <summary>
/// Lit une propriete de peripherique. Toutes les methodes rendent faux plutot que de
/// lever : une propriete absente est un cas courant, pas une erreur.
/// </summary>
public interface IDevicePropertyReader
{
    bool TryReadUInt32(string? instanceId, DeviceProperty property, out uint value);

    bool TryReadString(string? instanceId, DeviceProperty property, out string? value);
}
```

- [ ] **Step 6 : Écrire l'implémentation P/Invoke**

Create `src/ForgeConflict.Native/CfgMgrDevicePropertyReader.cs` :

```csharp
using System.Runtime.InteropServices;
using System.Text;

namespace ForgeConflict.Native;

/// <summary>
/// Lit les proprietes de peripherique par cfgmgr32. Aucune API managee n'expose la
/// largeur ni la vitesse du lien PCIe, d'ou le recours a l'interoperabilite native.
/// Tous les appels sont en LECTURE : ce type ne modifie jamais l'etat du systeme.
/// </summary>
public sealed class CfgMgrDevicePropertyReader : IDevicePropertyReader
{
    private const int CrSuccess = 0;
    private const int DevpropTypeUInt32 = 0x00000007;
    private const int DevpropTypeString = 0x00000012;

    public bool TryReadUInt32(string? instanceId, DeviceProperty property, out uint value)
    {
        value = 0;

        var buffer = ReadProperty(instanceId, property, out var type);
        if (buffer is null || type != DevpropTypeUInt32 || buffer.Length < sizeof(uint))
        {
            return false;
        }

        value = BitConverter.ToUInt32(buffer, 0);
        return true;
    }

    public bool TryReadString(string? instanceId, DeviceProperty property, out string? value)
    {
        value = null;

        var buffer = ReadProperty(instanceId, property, out var type);
        if (buffer is null || type != DevpropTypeString)
        {
            return false;
        }

        // Chaine UTF-16 terminee par un caractere nul, qu'il faut retirer.
        value = Encoding.Unicode.GetString(buffer).TrimEnd('\0');
        return !string.IsNullOrWhiteSpace(value);
    }

    private static byte[]? ReadProperty(string? instanceId, DeviceProperty property, out int propertyType)
    {
        propertyType = 0;
        if (string.IsNullOrWhiteSpace(instanceId)) return null;

        if (CM_Locate_DevNodeW(out var devInst, instanceId, 0) != CrSuccess)
        {
            return null;
        }

        var key = new DevPropKey { Category = property.Category, PropertyId = property.PropertyId };

        // Premier appel a taille nulle : Windows renseigne la taille necessaire.
        uint size = 0;
        CM_Get_DevNode_PropertyW(devInst, ref key, out propertyType, null, ref size, 0);
        if (size == 0) return null;

        var buffer = new byte[size];
        return CM_Get_DevNode_PropertyW(devInst, ref key, out propertyType, buffer, ref size, 0) == CrSuccess
            ? buffer
            : null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DevPropKey
    {
        public Guid Category;
        public uint PropertyId;
    }

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int CM_Locate_DevNodeW(out uint pdnDevInst, string pDeviceID, uint ulFlags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int CM_Get_DevNode_PropertyW(
        uint dnDevInst,
        ref DevPropKey propertyKey,
        out int propertyType,
        byte[]? propertyBuffer,
        ref uint propertyBufferSize,
        uint ulFlags);
}
```

- [ ] **Step 7 : Lancer les tests pour vérifier le succès**

Run : `dotnet test ForgeConflict.sln --filter FullyQualifiedName~DevicePropertyReaderTests`
Expected : `Passed! - Failed: 0, Passed: 4`.

Si `TryReadUInt32` rend faux sur un vrai adaptateur, comparer avec la sortie de référence :

```powershell
Get-PnpDeviceProperty -InstanceId '<id>' -KeyName DEVPKEY_PciDevice_CurrentLinkWidth
```

- [ ] **Step 8 : Commit**

```bash
git add src/ForgeConflict.Native tests/ForgeConflict.Tests
git commit -m "Add cfgmgr32 device property reader for PCIe link information"
```

---

### Task 2 : Modèles des sources natives

**Files:**
- Create: `src/ForgeConflict.Core/Inventory/Model/GpuEvent.cs`
- Create: `src/ForgeConflict.Core/Inventory/Model/CrashArtifact.cs`
- Create: `src/ForgeConflict.Core/Inventory/Model/PcieLink.cs`
- Create: `src/ForgeConflict.Core/Inventory/Model/PowerProfile.cs`
- Create: `src/ForgeConflict.Core/Inventory/Model/DisplayMode.cs`
- Test: `tests/ForgeConflict.Tests/Inventory/PcieLinkTests.cs`

- [ ] **Step 1 : Écrire les tests qui échouent**

Create `tests/ForgeConflict.Tests/Inventory/PcieLinkTests.cs` :

```csharp
using ForgeConflict.Core.Inventory.Model;

namespace ForgeConflict.Tests.Inventory;

public sealed class PcieLinkTests
{
    [Fact]
    public void IsDegraded_IsTrueWhenTheLinkRunsBelowItsMaximumWidth()
    {
        var link = new PcieLink(CurrentWidth: 8, MaxWidth: 16, CurrentSpeedGen: 4, MaxSpeedGen: 4);

        Assert.True(link.IsWidthDegraded);
        Assert.False(link.IsSpeedDegraded);
        Assert.True(link.IsDegraded);
    }

    [Fact]
    public void IsDegraded_IsFalseWhenTheLinkRunsAtItsMaximum()
    {
        var link = new PcieLink(16, 16, 4, 4);

        Assert.False(link.IsDegraded);
    }

    [Fact]
    public void IsDegraded_DetectsSpeedDropIndependentlyOfWidth()
    {
        var link = new PcieLink(16, 16, CurrentSpeedGen: 1, MaxSpeedGen: 4);

        Assert.True(link.IsSpeedDegraded);
        Assert.False(link.IsWidthDegraded);
    }

    [Fact]
    public void Describe_ReadsAsPlainPcieNotation()
    {
        var link = new PcieLink(8, 16, 4, 4);

        var text = link.Describe();

        Assert.Contains("x8", text, StringComparison.Ordinal);
        Assert.Contains("x16", text, StringComparison.Ordinal);
        Assert.Contains("Gen4", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_ReportsNoDegradationWhenNothingCouldBeRead()
    {
        // Une VM n'expose aucune propriete PCIe : l'absence d'information ne doit
        // jamais etre presentee comme une degradation.
        Assert.False(PcieLink.Unknown.IsDegraded);
        Assert.False(PcieLink.Unknown.IsKnown);
    }
}
```

- [ ] **Step 2 : Lancer les tests pour vérifier l'échec**

Run : `dotnet test ForgeConflict.sln --filter FullyQualifiedName~PcieLinkTests`
Expected : échec de compilation, `PcieLink` étant introuvable.

- [ ] **Step 3 : Écrire les modèles**

Create `src/ForgeConflict.Core/Inventory/Model/PcieLink.cs` :

```csharp
using System.Globalization;

namespace ForgeConflict.Core.Inventory.Model;

/// <summary>
/// Etat du lien PCIe d'un adaptateur. Une largeur inferieure au maximum bride la
/// bande passante ; sur portable c'est parfois le cablage, ailleurs le signe d'un
/// slot mal occupe ou d'un reglage d'economie d'energie.
/// </summary>
public sealed record PcieLink(uint CurrentWidth, uint MaxWidth, uint CurrentSpeedGen, uint MaxSpeedGen)
{
    /// <summary>Lien dont rien n'a pu etre lu, cas normal en machine virtuelle.</summary>
    public static PcieLink Unknown => new(0, 0, 0, 0);

    public bool IsKnown => CurrentWidth > 0 && MaxWidth > 0;

    public bool IsWidthDegraded => IsKnown && CurrentWidth < MaxWidth;

    public bool IsSpeedDegraded =>
        CurrentSpeedGen > 0 && MaxSpeedGen > 0 && CurrentSpeedGen < MaxSpeedGen;

    public bool IsDegraded => IsWidthDegraded || IsSpeedDegraded;

    public string Describe() => IsKnown
        ? string.Create(CultureInfo.InvariantCulture,
            $"x{CurrentWidth} Gen{CurrentSpeedGen} (maximum x{MaxWidth} Gen{MaxSpeedGen})")
        : "lien PCIe inconnu";
}
```

Create `src/ForgeConflict.Core/Inventory/Model/GpuEvent.cs` :

```csharp
namespace ForgeConflict.Core.Inventory.Model;

/// <summary>
/// Evenement du journal Windows attribue a un pilote graphique. Le fournisseur est
/// conserve : un identifiant d'evenement n'a de sens qu'associe a son fournisseur.
/// </summary>
public sealed record GpuEvent(
    DateTimeOffset TimeCreated,
    int EventId,
    string ProviderName,
    string? Level,
    string? Message)
{
    /// <summary>Reinitialisation du pilote apres depassement du delai de reponse (TDR).</summary>
    public bool IsTimeoutRecovery => EventId is 4101 or 4102;
}
```

Create `src/ForgeConflict.Core/Inventory/Model/CrashArtifact.cs` :

```csharp
namespace ForgeConflict.Core.Inventory.Model;

public enum CrashArtifactKind
{
    LiveKernelReport,
    Minidump,
}

public sealed record CrashArtifact(
    CrashArtifactKind Kind,
    string Path,
    string FileName,
    long SizeBytes,
    DateTimeOffset CreatedAt,
    string? Category);
```

Create `src/ForgeConflict.Core/Inventory/Model/PowerProfile.cs` :

```csharp
namespace ForgeConflict.Core.Inventory.Model;

public sealed record PowerProfile(
    string? ActiveSchemeGuid,
    string? ActiveSchemeName,
    RegistrySetting PciExpressLinkStateOnAc,
    RegistrySetting PciExpressLinkStateOnBattery);
```

Create `src/ForgeConflict.Core/Inventory/Model/DisplayMode.cs` :

```csharp
namespace ForgeConflict.Core.Inventory.Model;

public sealed record DisplayMode(
    string AdapterName,
    int HorizontalResolution,
    int VerticalResolution,
    int RefreshRateHz,
    string? Description)
{
    public bool IsActive => HorizontalResolution > 0 && VerticalResolution > 0;
}
```

- [ ] **Step 4 : Lancer les tests pour vérifier le succès**

Run : `dotnet test ForgeConflict.sln --filter FullyQualifiedName~PcieLinkTests`
Expected : `Passed! - Failed: 0, Passed: 5`.

- [ ] **Step 5 : Commit**

```bash
git add src/ForgeConflict.Core/Inventory/Model tests/ForgeConflict.Tests/Inventory
git commit -m "Add models for events, crash artifacts, PCIe link, power and displays"
```

---

### Task 3 : Collecteur d'événements GPU

**Files:**
- Create: `src/ForgeConflict.Core/Inventory/Collectors/GpuEventCollector.cs`
- Test: `tests/ForgeConflict.Tests/Inventory/GpuEventCollectorTests.cs`

- [ ] **Step 1 : Écrire les tests qui échouent**

Create `tests/ForgeConflict.Tests/Inventory/GpuEventCollectorTests.cs` :

```csharp
using ForgeConflict.Core.Inventory.Collectors;

namespace ForgeConflict.Tests.Inventory;

public sealed class GpuEventCollectorTests
{
    [Fact]
    public void BuildQuery_FiltersOnProviderAndIdTogether()
    {
        var query = GpuEventCollector.BuildQuery();

        // Un identifiant d'evenement n'est unique QUE par fournisseur : filtrer sur
        // l'identifiant seul ramene WindowsUpdateClient id 19 et BTHUSB id 18, sans
        // aucun rapport avec le GPU.
        Assert.Contains("Provider[", query, StringComparison.Ordinal);
        Assert.Contains("EventID=", query, StringComparison.Ordinal);
        Assert.Contains("nvlddmkm", query, StringComparison.Ordinal);
        Assert.Contains("amdkmdag", query, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildQuery_DoesNotSelectBareEventIdsWithoutAProvider()
    {
        var query = GpuEventCollector.BuildQuery();

        // Chaque clause EventID doit vivre dans un Provider[...] : aucun terme
        // EventID ne doit apparaitre en dehors d'un selecteur de fournisseur.
        var providerCount = System.Text.RegularExpressions.Regex.Matches(query, @"Provider\[").Count;
        Assert.True(providerCount > 0);
    }

    [Fact]
    public void Collect_ReturnsEmptyOnAHealthyMachineInsteadOfFailing()
    {
        // Aucune machine saine n'a d'evenement de plantage GPU. L'absence est un
        // resultat valide, jamais une erreur.
        var events = new GpuEventCollector().Collect(CancellationToken.None);

        Assert.NotNull(events);
        Assert.All(events, e => Assert.False(string.IsNullOrWhiteSpace(e.ProviderName)));
    }

    [Fact]
    public void Metadata_IsStableAndUsable()
    {
        var collector = new GpuEventCollector();

        Assert.Equal("evenements-gpu", collector.Source);
        Assert.True(collector.Timeout > TimeSpan.Zero);
    }
}
```

- [ ] **Step 2 : Lancer les tests pour vérifier l'échec**

Run : `dotnet test ForgeConflict.sln --filter FullyQualifiedName~GpuEventCollectorTests`
Expected : échec de compilation, `GpuEventCollector` étant introuvable.

- [ ] **Step 3 : Écrire le collecteur**

Create `src/ForgeConflict.Core/Inventory/Collectors/GpuEventCollector.cs` :

```csharp
using System.Diagnostics.Eventing.Reader;
using System.Text;
using ForgeConflict.Core.Inventory.Model;

namespace ForgeConflict.Core.Inventory.Collectors;

/// <summary>
/// Releve l'historique de plantage graphique dans le journal Windows.
///
/// Le filtre associe toujours un fournisseur a ses identifiants d'evenement : un
/// identifiant n'est unique que par fournisseur. Filtrer sur l'identifiant seul
/// ramenerait WindowsUpdateClient id 19 ou BTHUSB id 18, sans rapport avec le GPU.
/// </summary>
public sealed class GpuEventCollector : ICollector<IReadOnlyList<GpuEvent>>
{
    private const int MaxEvents = 200;

    /// <summary>Couples fournisseur / identifiants reellement pertinents pour le GPU.</summary>
    private static readonly (string Provider, int[] Ids)[] Sources =
    [
        // Reinitialisation du pilote apres depassement du delai (TDR).
        ("Display", [4101, 4102]),
        ("Microsoft-Windows-DxgKrnl", [4101, 4102]),
        // Erreurs remontees par le pilote noyau de chaque constructeur.
        ("nvlddmkm", []),
        ("amdkmdag", []),
        ("amdwddmg", []),
        ("igfxn", []),
        ("igdkmd64", []),
        // Erreurs materielles, notamment sur le bus PCIe.
        ("Microsoft-Windows-WHEA-Logger", []),
    ];

    public string Source => "evenements-gpu";

    public TimeSpan Timeout => TimeSpan.FromSeconds(30);

    /// <summary>Construit la requete XPath du journal. Exposee pour etre verifiee par les tests.</summary>
    public static string BuildQuery()
    {
        var clauses = new List<string>();

        foreach (var (provider, ids) in Sources)
        {
            if (ids.Length == 0)
            {
                clauses.Add($"Provider[@Name='{provider}']");
                continue;
            }

            var idClause = string.Join(" or ", ids.Select(id => $"EventID={id}"));
            clauses.Add($"Provider[@Name='{provider}'] and ({idClause})");
        }

        var builder = new StringBuilder("*[System[");
        builder.Append(string.Join(" or ", clauses.Select(c => $"({c})")));
        builder.Append("]]");
        return builder.ToString();
    }

    public IReadOnlyList<GpuEvent> Collect(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var events = new List<GpuEvent>();
        var query = new EventLogQuery("System", PathType.LogName, BuildQuery())
        {
            ReverseDirection = true, // du plus recent au plus ancien
        };

        using var reader = new EventLogReader(query);

        for (var i = 0; i < MaxEvents; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            EventRecord? record;
            try
            {
                record = reader.ReadEvent();
            }
            catch (EventLogException)
            {
                // Journal indisponible ou tronque : on rend ce qui a ete lu.
                break;
            }

            if (record is null) break;

            using (record)
            {
                string? message = null;
                try
                {
                    // Le rendu du message echoue si le fournisseur est desinstalle,
                    // ce qui arrive precisement apres une desinstallation de pilote.
                    message = record.FormatDescription();
                }
                catch (EventLogException)
                {
                }

                events.Add(new GpuEvent(
                    TimeCreated: record.TimeCreated is { } time
                        ? new DateTimeOffset(time)
                        : DateTimeOffset.MinValue,
                    EventId: record.Id,
                    ProviderName: record.ProviderName ?? "inconnu",
                    Level: record.LevelDisplayName,
                    Message: message));
            }
        }

        return events;
    }
}
```

- [ ] **Step 4 : Lancer les tests pour vérifier le succès**

Run : `dotnet test ForgeConflict.sln --filter FullyQualifiedName~GpuEventCollectorTests`
Expected : `Passed! - Failed: 0, Passed: 4`.

- [ ] **Step 5 : Vérifier que la requête ne ramène pas de bruit**

La machine de référence n'a aucun événement GPU : un résultat vide est attendu. Contrôler qu'aucun
fournisseur hors sujet n'apparaît si la liste n'est pas vide :

```powershell
dotnet run --project src/ForgeConflict.App 2>$null
```

Ou plus simplement, comparer avec la sonde manuelle :

```powershell
Get-WinEvent -FilterHashtable @{LogName='System'; ProviderName='Display'} -MaxEvents 5 -ErrorAction SilentlyContinue |
  Select-Object Id, ProviderName, TimeCreated
```

Expected : aucune ligne portant `WindowsUpdateClient`, `BTHUSB` ou `TPM`.

- [ ] **Step 6 : Commit**

```bash
git add src/ForgeConflict.Core/Inventory/Collectors tests/ForgeConflict.Tests/Inventory
git commit -m "Add GPU event collector filtering on provider and event id together"
```

---

### Task 4 : Collecteur de traces de plantage

**Files:**
- Create: `src/ForgeConflict.Core/Inventory/Collectors/CrashArtifactCollector.cs`
- Test: `tests/ForgeConflict.Tests/Inventory/CrashArtifactCollectorTests.cs`

- [ ] **Step 1 : Écrire les tests qui échouent**

Create `tests/ForgeConflict.Tests/Inventory/CrashArtifactCollectorTests.cs` :

```csharp
using ForgeConflict.Core.Inventory.Collectors;
using ForgeConflict.Core.Inventory.Model;

namespace ForgeConflict.Tests.Inventory;

public sealed class CrashArtifactCollectorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fc-crash", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Scan_ReadsTheCategoryFromTheContainingFolder()
    {
        var watchdog = Path.Combine(_root, "WATCHDOG");
        Directory.CreateDirectory(watchdog);
        File.WriteAllText(Path.Combine(watchdog, "WATCHDOG-20260806-0001.dmp"), "contenu");

        var artifacts = CrashArtifactCollector.Scan(_root, CrashArtifactKind.LiveKernelReport);

        var artifact = Assert.Single(artifacts);
        Assert.Equal("WATCHDOG", artifact.Category);
        Assert.Equal(CrashArtifactKind.LiveKernelReport, artifact.Kind);
        Assert.True(artifact.SizeBytes > 0);
    }

    [Fact]
    public void Scan_ReturnsEmptyWhenTheFolderDoesNotExist()
    {
        // Cas normal sur une machine saine : ni LiveKernelReports ni Minidump.
        var artifacts = CrashArtifactCollector.Scan(
            Path.Combine(_root, "absent"), CrashArtifactKind.Minidump);

        Assert.Empty(artifacts);
    }

    [Fact]
    public void Scan_IgnoresFilesThatAreNotDumps()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "notes.txt"), "pas un dump");
        File.WriteAllText(Path.Combine(_root, "crash.dmp"), "dump");

        var artifacts = CrashArtifactCollector.Scan(_root, CrashArtifactKind.Minidump);

        var artifact = Assert.Single(artifacts);
        Assert.Equal("crash.dmp", artifact.FileName);
    }

    [Fact]
    public void Collect_NeverThrowsOnTheRealMachine()
    {
        var artifacts = new CrashArtifactCollector().Collect(CancellationToken.None);

        Assert.NotNull(artifacts);
        Assert.All(artifacts, a => Assert.False(string.IsNullOrWhiteSpace(a.FileName)));
    }

    [Fact]
    public void Metadata_IsStableAndUsable()
    {
        var collector = new CrashArtifactCollector();

        Assert.Equal("traces-plantage", collector.Source);
        Assert.True(collector.Timeout > TimeSpan.Zero);
    }
}
```

- [ ] **Step 2 : Lancer les tests pour vérifier l'échec**

Run : `dotnet test ForgeConflict.sln --filter FullyQualifiedName~CrashArtifactCollectorTests`
Expected : échec de compilation, `CrashArtifactCollector` étant introuvable.

- [ ] **Step 3 : Écrire le collecteur**

Create `src/ForgeConflict.Core/Inventory/Collectors/CrashArtifactCollector.cs` :

```csharp
using ForgeConflict.Core.Inventory.Model;

namespace ForgeConflict.Core.Inventory.Collectors;

/// <summary>
/// Recense les traces de plantage noyau laissees par un pilote graphique.
/// Une machine saine n'en a aucune : l'absence est un resultat normal.
/// </summary>
public sealed class CrashArtifactCollector : ICollector<IReadOnlyList<CrashArtifact>>
{
    private static readonly string[] DumpExtensions = [".dmp", ".mdmp"];

    public string Source => "traces-plantage";

    public TimeSpan Timeout => TimeSpan.FromSeconds(20);

    public IReadOnlyList<CrashArtifact> Collect(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        var artifacts = new List<CrashArtifact>();
        artifacts.AddRange(Scan(Path.Combine(windows, "LiveKernelReports"), CrashArtifactKind.LiveKernelReport));
        artifacts.AddRange(Scan(Path.Combine(windows, "Minidump"), CrashArtifactKind.Minidump));

        return artifacts;
    }

    /// <summary>
    /// Parcourt un dossier de dumps. La categorie est deduite du dossier parent :
    /// LiveKernelReports range ses traces par type, ex. WATCHDOG ou DXGKRNL.
    /// </summary>
    public static IReadOnlyList<CrashArtifact> Scan(string directory, CrashArtifactKind kind)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return [];

        var artifacts = new List<CrashArtifact>();

        try
        {
            foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                if (!DumpExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                FileInfo info;
                try
                {
                    info = new FileInfo(path);
                    if (!info.Exists) continue;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                var parent = Path.GetDirectoryName(path);
                var category = string.Equals(parent, directory, StringComparison.OrdinalIgnoreCase)
                    ? null
                    : Path.GetFileName(parent);

                artifacts.Add(new CrashArtifact(
                    Kind: kind,
                    Path: path,
                    FileName: info.Name,
                    SizeBytes: info.Length,
                    CreatedAt: new DateTimeOffset(info.CreationTimeUtc, TimeSpan.Zero),
                    Category: category));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Dossier protege ou verrouille : on rend ce qui a pu etre lu.
        }

        return artifacts;
    }
}
```

- [ ] **Step 4 : Lancer les tests pour vérifier le succès**

Run : `dotnet test ForgeConflict.sln --filter FullyQualifiedName~CrashArtifactCollectorTests`
Expected : `Passed! - Failed: 0, Passed: 5`.

- [ ] **Step 5 : Commit**

```bash
git add src/ForgeConflict.Core/Inventory/Collectors tests/ForgeConflict.Tests/Inventory
git commit -m "Add crash artifact collector for live kernel reports and minidumps"
```

---

### Task 5 : Collecteur matériel PCIe

**Files:**
- Create: `src/ForgeConflict.Core/Inventory/Collectors/PcieCollector.cs`
- Test: `tests/ForgeConflict.Tests/Inventory/PcieCollectorTests.cs`

- [ ] **Step 1 : Écrire les tests qui échouent**

Create `tests/ForgeConflict.Tests/Inventory/PcieCollectorTests.cs` :

```csharp
using ForgeConflict.Core.Inventory.Collectors;
using ForgeConflict.Core.Inventory.Model;
using ForgeConflict.Native;

namespace ForgeConflict.Tests.Inventory;

public sealed class PcieCollectorTests
{
    private sealed class FakeReader : IDevicePropertyReader
    {
        private readonly Dictionary<uint, uint> _values;

        public FakeReader(Dictionary<uint, uint> values) => _values = values;

        public bool TryReadUInt32(string? instanceId, DeviceProperty property, out uint value) =>
            _values.TryGetValue(property.PropertyId, out value);

        public bool TryReadString(string? instanceId, DeviceProperty property, out string? value)
        {
            value = null;
            return false;
        }
    }

    [Fact]
    public void ReadLink_BuildsADegradedLinkFromDeviceProperties()
    {
        var reader = new FakeReader(new Dictionary<uint, uint>
        {
            [10] = 8,   // CurrentLinkWidth
            [12] = 16,  // MaxLinkWidth
            [9] = 4,    // CurrentLinkSpeed
            [11] = 4,   // MaxLinkSpeed
        });

        var link = PcieCollector.ReadLink(reader, @"PCI\VEN_10DE&DEV_249D");

        Assert.True(link.IsKnown);
        Assert.True(link.IsWidthDegraded);
        Assert.Equal(8u, link.CurrentWidth);
        Assert.Equal(16u, link.MaxWidth);
    }

    [Fact]
    public void ReadLink_ReturnsUnknownWhenNoPropertyCanBeRead()
    {
        // Cas d'une machine virtuelle : aucune propriete PCIe n'est exposee.
        var link = PcieCollector.ReadLink(new FakeReader([]), @"ROOT\BASICDISPLAY");

        Assert.False(link.IsKnown);
        Assert.False(link.IsDegraded);
        Assert.Equal("lien PCIe inconnu", link.Describe());
    }

    [Fact]
    public void Collect_ReportsOneEntryPerAdapterOnTheRealMachine()
    {
        var links = new PcieCollector().Collect(CancellationToken.None);

        Assert.NotNull(links);
        Assert.All(links, l => Assert.False(string.IsNullOrWhiteSpace(l.Key)));
    }

    [Fact]
    public void Metadata_IsStableAndUsable()
    {
        var collector = new PcieCollector();

        Assert.Equal("materiel-pcie", collector.Source);
        Assert.True(collector.Timeout > TimeSpan.Zero);
    }
}
```

- [ ] **Step 2 : Lancer les tests pour vérifier l'échec**

Run : `dotnet test ForgeConflict.sln --filter FullyQualifiedName~PcieCollectorTests`
Expected : échec de compilation, `PcieCollector` étant introuvable.

- [ ] **Step 3 : Écrire le collecteur**

Create `src/ForgeConflict.Core/Inventory/Collectors/PcieCollector.cs` :

```csharp
using System.Management;
using ForgeConflict.Core.Inventory.Model;
using ForgeConflict.Native;

namespace ForgeConflict.Core.Inventory.Collectors;

/// <summary>
/// Releve l'etat du lien PCIe de chaque adaptateur graphique. Les proprietes sont
/// lues par cfgmgr32 : aucune API managee ne les expose.
/// </summary>
public sealed class PcieCollector : ICollector<IReadOnlyList<KeyValuePair<string, PcieLink>>>
{
    private readonly IDevicePropertyReader _reader;

    public PcieCollector() : this(new CfgMgrDevicePropertyReader())
    {
    }

    public PcieCollector(IDevicePropertyReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        _reader = reader;
    }

    public string Source => "materiel-pcie";

    public TimeSpan Timeout => TimeSpan.FromSeconds(20);

    /// <summary>
    /// Assemble l'etat du lien. Si aucune propriete n'est lisible, rend
    /// <see cref="PcieLink.Unknown"/> : une information absente ne doit jamais
    /// etre presentee comme une degradation.
    /// </summary>
    public static PcieLink ReadLink(IDevicePropertyReader reader, string instanceId)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var hasWidth = reader.TryReadUInt32(instanceId, DeviceProperty.PciCurrentLinkWidth, out var currentWidth);
        reader.TryReadUInt32(instanceId, DeviceProperty.PciMaxLinkWidth, out var maxWidth);
        reader.TryReadUInt32(instanceId, DeviceProperty.PciCurrentLinkSpeed, out var currentSpeed);
        reader.TryReadUInt32(instanceId, DeviceProperty.PciMaxLinkSpeed, out var maxSpeed);

        return hasWidth
            ? new PcieLink(currentWidth, maxWidth, currentSpeed, maxSpeed)
            : PcieLink.Unknown;
    }

    public IReadOnlyList<KeyValuePair<string, PcieLink>> Collect(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var links = new List<KeyValuePair<string, PcieLink>>();

        using var searcher = new ManagementObjectSearcher(
            "SELECT Name, PNPDeviceID FROM Win32_VideoController");
        using var rows = searcher.Get();

        foreach (var item in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var row = item;

            var name = row["Name"]?.ToString() ?? "Adaptateur inconnu";
            var instanceId = row["PNPDeviceID"]?.ToString();

            links.Add(new KeyValuePair<string, PcieLink>(name, ReadLink(_reader, instanceId ?? string.Empty)));
        }

        return links;
    }
}
```

- [ ] **Step 4 : Lancer les tests pour vérifier le succès**

Run : `dotnet test ForgeConflict.sln --filter FullyQualifiedName~PcieCollectorTests`
Expected : `Passed! - Failed: 0, Passed: 4`.

- [ ] **Step 5 : Confronter à la sonde de référence**

La machine de référence rapporte le RTX 3070 en x8 pour un maximum x16. Confirmer que le collecteur
lit les mêmes valeurs :

```powershell
Get-PnpDeviceProperty -InstanceId (Get-CimInstance Win32_VideoController | Where-Object Name -match NVIDIA).PNPDeviceID `
  -KeyName DEVPKEY_PciDevice_CurrentLinkWidth, DEVPKEY_PciDevice_MaxLinkWidth |
  Select-Object KeyName, Data
```

Expected : `CurrentLinkWidth = 8`, `MaxLinkWidth = 16`.

- [ ] **Step 6 : Commit**

```bash
git add src/ForgeConflict.Core/Inventory/Collectors tests/ForgeConflict.Tests/Inventory
git commit -m "Add PCIe collector reporting link width and speed per adapter"
```

---

### Task 6 : Collecteurs alimentation et écrans

**Files:**
- Create: `src/ForgeConflict.Core/Inventory/Collectors/PowerCollector.cs`
- Create: `src/ForgeConflict.Core/Inventory/Collectors/DisplayCollector.cs`
- Test: `tests/ForgeConflict.Tests/Inventory/PowerAndDisplayCollectorTests.cs`

- [ ] **Step 1 : Écrire les tests qui échouent**

Create `tests/ForgeConflict.Tests/Inventory/PowerAndDisplayCollectorTests.cs` :

```csharp
using ForgeConflict.Core.Inventory.Collectors;

namespace ForgeConflict.Tests.Inventory;

public sealed class PowerAndDisplayCollectorTests
{
    [Fact]
    public void PowerCollector_ReadsTheActiveSchemeOnTheRealMachine()
    {
        var profile = new PowerCollector().Collect(CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(profile.ActiveSchemeGuid));
        Assert.False(string.IsNullOrWhiteSpace(profile.ActiveSchemeName));
    }

    [Fact]
    public void PowerCollector_NeverReportsAbsentLinkStateAsZero()
    {
        var profile = new PowerCollector().Collect(CancellationToken.None);

        // La valeur par defaut est l'economie maximale (2) : la lire comme zero
        // ferait croire que l'economie d'energie PCIe est desactivee.
        if (!profile.PciExpressLinkStateOnAc.IsPresent)
        {
            Assert.NotNull(profile.PciExpressLinkStateOnAc.EffectiveValue);
        }
    }

    [Fact]
    public void PowerCollector_MetadataIsStable()
    {
        var collector = new PowerCollector();

        Assert.Equal("alimentation", collector.Source);
        Assert.True(collector.Timeout > TimeSpan.Zero);
    }

    [Fact]
    public void DisplayCollector_ReportsActiveModesOnTheRealMachine()
    {
        var modes = new DisplayCollector().Collect(CancellationToken.None);

        Assert.NotEmpty(modes);
        Assert.All(modes, m => Assert.False(string.IsNullOrWhiteSpace(m.AdapterName)));

        // Au moins un adaptateur pilote un affichage : sinon rien ne serait visible.
        Assert.Contains(modes, m => m.IsActive);
    }

    [Fact]
    public void DisplayCollector_MetadataIsStable()
    {
        var collector = new DisplayCollector();

        Assert.Equal("ecrans", collector.Source);
        Assert.True(collector.Timeout > TimeSpan.Zero);
    }
}
```

- [ ] **Step 2 : Lancer les tests pour vérifier l'échec**

Run : `dotnet test ForgeConflict.sln --filter FullyQualifiedName~PowerAndDisplayCollectorTests`
Expected : échec de compilation, `PowerCollector` et `DisplayCollector` étant introuvables.

- [ ] **Step 3 : Écrire le collecteur d'alimentation**

Create `src/ForgeConflict.Core/Inventory/Collectors/PowerCollector.cs` :

```csharp
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
```

- [ ] **Step 4 : Écrire le collecteur d'écrans**

Create `src/ForgeConflict.Core/Inventory/Collectors/DisplayCollector.cs` :

```csharp
using System.Globalization;
using System.Management;
using ForgeConflict.Core.Inventory.Model;

namespace ForgeConflict.Core.Inventory.Collectors;

/// <summary>
/// Releve le mode d'affichage courant de chaque adaptateur. En configuration hybride
/// les modes different d'un adaptateur a l'autre, ce qui est normal et ne constitue
/// pas une anomalie en soi.
/// </summary>
public sealed class DisplayCollector : ICollector<IReadOnlyList<DisplayMode>>
{
    public string Source => "ecrans";

    public TimeSpan Timeout => TimeSpan.FromSeconds(20);

    public IReadOnlyList<DisplayMode> Collect(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var modes = new List<DisplayMode>();

        using var searcher = new ManagementObjectSearcher(
            "SELECT Name, CurrentHorizontalResolution, CurrentVerticalResolution, CurrentRefreshRate, VideoModeDescription FROM Win32_VideoController");
        using var rows = searcher.Get();

        foreach (var item in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var row = item;

            modes.Add(new DisplayMode(
                AdapterName: row["Name"]?.ToString() ?? "Adaptateur inconnu",
                HorizontalResolution: ToInt(row["CurrentHorizontalResolution"]),
                VerticalResolution: ToInt(row["CurrentVerticalResolution"]),
                RefreshRateHz: ToInt(row["CurrentRefreshRate"]),
                Description: row["VideoModeDescription"]?.ToString()));
        }

        return modes;
    }

    private static int ToInt(object? value) =>
        value is null ? 0 : Convert.ToInt32(value, CultureInfo.InvariantCulture);
}
```

- [ ] **Step 5 : Lancer les tests pour vérifier le succès**

Run : `dotnet test ForgeConflict.sln --filter FullyQualifiedName~PowerAndDisplayCollectorTests`
Expected : `Passed! - Failed: 0, Passed: 5`.

- [ ] **Step 6 : Commit**

```bash
git add src/ForgeConflict.Core/Inventory/Collectors tests/ForgeConflict.Tests/Inventory
git commit -m "Add power profile and display mode collectors"
```

---

### Task 7 : Intégration des dix sources

**Files:**
- Modify: `src/ForgeConflict.Core/Inventory/SystemSnapshot.cs`
- Modify: `src/ForgeConflict.Core/Inventory/InventoryScanner.cs`
- Modify: `src/ForgeConflict.Core/Inventory/SnapshotReport.cs`
- Modify: `tests/ForgeConflict.Tests/Inventory/InventoryScannerTests.cs`
- Modify: `Directory.Build.props`

- [ ] **Step 1 : Étendre l'instantané**

Modify `src/ForgeConflict.Core/Inventory/SystemSnapshot.cs` — ajouter les cinq champs après
`OverlayProcesses` dans la signature du record :

```csharp
    Fragment<IReadOnlyList<ProcessInfo>> OverlayProcesses,
    Fragment<IReadOnlyList<GpuEvent>> GpuEvents,
    Fragment<IReadOnlyList<CrashArtifact>> CrashArtifacts,
    Fragment<IReadOnlyList<KeyValuePair<string, PcieLink>>> PcieLinks,
    Fragment<PowerProfile> Power,
    Fragment<IReadOnlyList<DisplayMode>> Displays)
```

Et remplacer `AllFragments` :

```csharp
    public IReadOnlyList<IFragment> AllFragments =>
    [
        Gpus, DriverPackages, Services, GraphicsSettings, OverlayProcesses,
        GpuEvents, CrashArtifacts, PcieLinks, Power, Displays,
    ];
```

- [ ] **Step 2 : Étendre l'orchestrateur**

Modify `src/ForgeConflict.Core/Inventory/InventoryScanner.cs` — ajouter les cinq collecteurs en
champs, puis les cinq tâches dans `ScanAsync`, et les passer au constructeur du `SystemSnapshot` :

```csharp
    private readonly GpuEventCollector _events = new();
    private readonly CrashArtifactCollector _crashes = new();
    private readonly PcieCollector _pcie = new();
    private readonly PowerCollector _power = new();
    private readonly DisplayCollector _displays = new();
```

Dans `ScanAsync`, après `overlays` :

```csharp
        var events = Run<IReadOnlyList<GpuEvent>>(_events, cancellationToken);
        var crashes = Run<IReadOnlyList<CrashArtifact>>(_crashes, cancellationToken);
        var pcie = Run<IReadOnlyList<KeyValuePair<string, PcieLink>>>(_pcie, cancellationToken);
        var power = Run<PowerProfile>(_power, cancellationToken);
        var displays = Run<IReadOnlyList<DisplayMode>>(_displays, cancellationToken);

        await Task.WhenAll(
            gpus, packages, services, settings, overlays,
            events, crashes, pcie, power, displays).ConfigureAwait(false);
```

Et compléter la construction du résultat :

```csharp
            OverlayProcesses: overlays.Result,
            GpuEvents: events.Result,
            CrashArtifacts: crashes.Result,
            PcieLinks: pcie.Result,
            Power: power.Result,
            Displays: displays.Result);
```

- [ ] **Step 3 : Étendre le rapport**

Modify `src/ForgeConflict.Core/Inventory/SnapshotReport.cs` — insérer avant le bloc
`UnusableSources`, puis ajouter les cinq enregistrements dans `ToEnvelope` :

```csharp
        if (snapshot.PcieLinks.IsUsable && snapshot.PcieLinks.Value is { } links)
        {
            text.AppendLine("--- Liens PCIe ---");
            foreach (var (adapter, link) in links)
            {
                var flag = link.IsDegraded ? "  [DEGRADE]" : string.Empty;
                text.AppendLine(CultureInfo.CurrentCulture, $"  {adapter} : {link.Describe()}{flag}");
            }
            text.AppendLine();
        }

        if (snapshot.Displays.IsUsable && snapshot.Displays.Value is { } displays)
        {
            text.AppendLine("--- Modes d'affichage ---");
            foreach (var mode in displays)
            {
                text.AppendLine(CultureInfo.CurrentCulture,
                    $"  {mode.AdapterName} : {mode.HorizontalResolution}x{mode.VerticalResolution} a {mode.RefreshRateHz} Hz");
            }
            text.AppendLine();
        }

        if (snapshot.Power.IsUsable && snapshot.Power.Value is { } power)
        {
            text.AppendLine("--- Alimentation ---");
            text.AppendLine(CultureInfo.CurrentCulture,
                $"  Plan actif : {power.ActiveSchemeName ?? "?"} ({power.ActiveSchemeGuid ?? "?"})");
            text.AppendLine(CultureInfo.CurrentCulture, $"  {power.PciExpressLinkStateOnAc.Describe()} (secteur)");
            text.AppendLine(CultureInfo.CurrentCulture, $"  {power.PciExpressLinkStateOnBattery.Describe()} (batterie)");
            text.AppendLine();
        }

        if (snapshot.GpuEvents.IsUsable && snapshot.GpuEvents.Value is { } events)
        {
            text.AppendLine(CultureInfo.CurrentCulture, $"--- Evenements GPU : {events.Count} ---");
            foreach (var gpuEvent in events.Take(10))
            {
                text.AppendLine(CultureInfo.CurrentCulture,
                    $"  {gpuEvent.TimeCreated:g} [{gpuEvent.ProviderName}] id {gpuEvent.EventId} {gpuEvent.Level}");
            }
            if (events.Count == 0) text.AppendLine("  Aucun incident graphique enregistre.");
            text.AppendLine();
        }

        if (snapshot.CrashArtifacts.IsUsable && snapshot.CrashArtifacts.Value is { } crashes)
        {
            text.AppendLine(CultureInfo.CurrentCulture, $"--- Traces de plantage : {crashes.Count} ---");
            foreach (var artifact in crashes.Take(10))
            {
                text.AppendLine(CultureInfo.CurrentCulture,
                    $"  [{artifact.Kind}] {artifact.Category ?? "-"} {artifact.FileName} ({artifact.SizeBytes / 1024} Ko)");
            }
            if (crashes.Count == 0) text.AppendLine("  Aucun dump de plantage present.");
            text.AppendLine();
        }
```

Dans `ToEnvelope`, compléter la liste :

```csharp
            Record(snapshot.OverlayProcesses),
            Record(snapshot.GpuEvents),
            Record(snapshot.CrashArtifacts),
            Record(snapshot.PcieLinks),
            Record(snapshot.Power),
            Record(snapshot.Displays),
```

- [ ] **Step 4 : Mettre les tests d'orchestration au nouveau compte**

Modify `tests/ForgeConflict.Tests/Inventory/InventoryScannerTests.cs` — remplacer les deux
occurrences du compte attendu :

```csharp
        Assert.Equal(10, snapshot.AllFragments.Count);
```

Et dans `SnapshotReportTests.ToEnvelope_ProducesSerializableCapture` :

```csharp
        Assert.Equal(10, restored.Fragments.Count);
```

- [ ] **Step 5 : Passer la version à 0.3.0**

Modify `Directory.Build.props` :

```xml
    <Version>0.3.0</Version>
```

- [ ] **Step 6 : Lancer toute la suite**

Run : `dotnet test ForgeConflict.sln --nologo`
Expected : `Failed: 0`, tous les tests des lots 1A, 1B et 1C.

- [ ] **Step 7 : Publier et vérifier sur la machine réelle**

Run : `powershell -ExecutionPolicy Bypass -File .\build.ps1`
Expected : `ForgeConflict 0.3.0`, `0 fichier(s) annexe(s)`.

Lancer et cliquer « Analyser la machine ». Vérifier :
1. Le RTX 3070 apparaît en `x8 Gen4 (maximum x16 Gen4)` avec la mention `[DEGRADE]` ;
2. Les modes d'affichage listent 1920×1080 à 144 Hz et 2560×1440 à 143 Hz ;
3. Les événements GPU et les traces de plantage annoncent explicitement l'absence d'incident ;
4. Les dix sources répondent.

- [ ] **Step 8 : Vérifier dans la VM**

Déposer l'exécutable puis lancer la vérification depuis l'invité :

```powershell
Copy-VMFile -Name 'W11 dev' -SourcePath 'D:\Projet\ForgeConflict\publish\ForgeConflict.exe' `
            -DestinationPath 'C:\ForgeConflict\ForgeConflict.exe' -FileSource Host -Force
```

Attendu : l'adaptateur Hyper-V rend `lien PCIe inconnu` sans être signalé comme dégradé, et les dix
sources répondent malgré l'absence de matériel réel. Récupérer la preuve avec
`tools/read-vm-report.ps1`.

- [ ] **Step 9 : Commit et push**

```bash
git add -A
git commit -m "Complete the inventory with the five native sources; bump to 0.3.0"
git push origin lot1a-socle
```

---

## Ce que ce lot ne fait pas

Aucune règle, aucune correction. L'inventaire des dix sources de la section 4 est complet ; le
lot 2 habille l'interface en Fluent 2 et le lot 4 introduit le moteur de règles.
