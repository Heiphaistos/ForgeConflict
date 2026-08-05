# ForgeConflict — Lot 1B : inventaire managé

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Collecter l'état graphique réel de la machine — GPU, pilotes, services, réglages registre, processus — en lecture stricte, sans qu'aucune source défaillante n'interrompe le scan.

**Architecture:** Chaque collecteur implémente `ICollector<T>`, expose une responsabilité unique et un délai propre. `InventoryScanner` les exécute en parallèle à travers l'`IsolatedRunner` du lot 1A et agrège le tout en `SystemSnapshot`. Aucun code d'écriture dans cet assemblage.

**Tech Stack:** .NET 8, `System.Management` (WMI), `Microsoft.Win32.Registry`, xUnit.

**Référence:** `docs/superpowers/specs/2026-08-05-forgeconflict-design.md` sections 3 et 4.

**Version :** 0.1.0 → 0.2.0

---

## Décisions issues de la reconnaissance du 06/08/2026

Trois constats sur la machine de référence conditionnent la conception. Ils ont été vérifiés, pas
supposés.

**La sortie texte de `pnputil` est localisée.** Sur un Windows français elle affiche
« Nom publié », « Nom du fournisseur ». La parser produirait un collecteur qui échoue sur tout
Windows anglais. Deux sources structurées et indépendantes de la langue sont retenues :
`Win32_PnPSignedDriver` (WMI) et `pnputil /enum-drivers /format xml`, dont les noms d'éléments
sont en anglais quelle que soit la langue du système.

**Les clés de réglage graphique sont absentes quand la valeur est celle par défaut.**
`TdrDelay`, `TdrLevel` et `HwSchMode` n'existent pas sur la machine de référence. Une règle qui
lirait « absent » comme « zéro » conclurait à un TDR désactivé, ce qui est faux et dangereux. Le
modèle distingue donc explicitement *absent* de *présent avec valeur*.

**Le fournisseur se lit dans l'identifiant PCI, pas dans le nom.** `PCI\VEN_10DE&DEV_249D` est
fiable ; « NVIDIA GeForce » dans une chaîne de description ne l'est pas. Les identifiants
constatés : `8086` Intel, `10DE` NVIDIA, `1002` et `1022` AMD.

---

### Task 1 : Contrat de collecte

**Files:**
- Create: `src/ForgeConflict.Core/Inventory/ICollector.cs`
- Create: `src/ForgeConflict.Core/Inventory/GpuVendor.cs`
- Test: `tests/ForgeConflict.Tests/Inventory/GpuVendorTests.cs`

- [ ] **Step 1 : Écrire le test qui échoue**

Create `tests/ForgeConflict.Tests/Inventory/GpuVendorTests.cs` :

```csharp
using ForgeConflict.Core.Inventory;

namespace ForgeConflict.Tests.Inventory;

public sealed class GpuVendorTests
{
    [Theory]
    [InlineData(@"PCI\VEN_10DE&DEV_249D&SUBSYS_15441025&REV_A1\4&3216B86&0&0008", GpuVendor.Nvidia)]
    [InlineData(@"PCI\VEN_8086&DEV_9A68&SUBSYS_153B1025&REV_01\3&11583659&0&10", GpuVendor.Intel)]
    [InlineData(@"PCI\VEN_1002&DEV_73FF", GpuVendor.Amd)]
    [InlineData(@"PCI\VEN_1022&DEV_1636", GpuVendor.Amd)]
    [InlineData(@"ROOT\BASICDISPLAY", GpuVendor.Unknown)]
    public void FromDeviceId_ReadsVendorFromPciIdentifier(string deviceId, GpuVendor expected) =>
        Assert.Equal(expected, GpuVendorReader.FromDeviceId(deviceId));

    [Fact]
    public void FromDeviceId_IsCaseInsensitive() =>
        Assert.Equal(GpuVendor.Nvidia, GpuVendorReader.FromDeviceId(@"pci\ven_10de&dev_249d"));

    [Fact]
    public void FromDeviceId_TreatsNullOrEmptyAsUnknown()
    {
        Assert.Equal(GpuVendor.Unknown, GpuVendorReader.FromDeviceId(null));
        Assert.Equal(GpuVendor.Unknown, GpuVendorReader.FromDeviceId(""));
    }

    [Fact]
    public void FromDeviceId_DoesNotGuessFromMarketingName()
    {
        // Le nom commercial n'est jamais une source : seul l'identifiant PCI fait foi.
        Assert.Equal(GpuVendor.Unknown, GpuVendorReader.FromDeviceId("NVIDIA GeForce RTX 3070"));
    }
}
```

- [ ] **Step 2 : Lancer le test pour vérifier l'échec**

Run : `dotnet test ForgeConflict.sln --filter FullyQualifiedName~GpuVendorTests`
Expected : échec de compilation, `GpuVendor` et `GpuVendorReader` étant introuvables.

- [ ] **Step 3 : Écrire le fournisseur**

Create `src/ForgeConflict.Core/Inventory/GpuVendor.cs` :

```csharp
using System.Text.RegularExpressions;

namespace ForgeConflict.Core.Inventory;

public enum GpuVendor
{
    Unknown,
    Nvidia,
    Amd,
    Intel,
    Microsoft,
}

/// <summary>
/// Deduit le fournisseur du seul identifiant PCI. Le nom commercial n'est jamais
/// utilise : il est libre, traduit et parfois trompeur sur les portables hybrides.
/// </summary>
public static partial class GpuVendorReader
{
    public static GpuVendor FromDeviceId(string? deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) return GpuVendor.Unknown;

        var match = VendorPattern().Match(deviceId);
        if (!match.Success) return GpuVendor.Unknown;

        return match.Groups[1].Value.ToUpperInvariant() switch
        {
            "10DE" => GpuVendor.Nvidia,
            "1002" or "1022" => GpuVendor.Amd,
            "8086" => GpuVendor.Intel,
            "1414" => GpuVendor.Microsoft,
            _ => GpuVendor.Unknown,
        };
    }

    [GeneratedRegex(@"VEN_([0-9A-Fa-f]{4})", RegexOptions.IgnoreCase)]
    private static partial Regex VendorPattern();
}
```

- [ ] **Step 4 : Écrire le contrat de collecteur**

Create `src/ForgeConflict.Core/Inventory/ICollector.cs` :

```csharp
namespace ForgeConflict.Core.Inventory;

/// <summary>
/// Source d'inventaire. Un collecteur LIT l'etat du systeme et ne le modifie jamais :
/// cet assemblage ne contient aucun code d'ecriture, par construction.
/// </summary>
public interface ICollector<out T>
{
    /// <summary>Identifiant court, repris tel quel dans le journal et dans l'interface.</summary>
    string Source { get; }

    /// <summary>Delai au-dela duquel la collecte est abandonnee. WMI peut bloquer sans fin.</summary>
    TimeSpan Timeout { get; }

    T Collect(CancellationToken cancellationToken);
}
```

- [ ] **Step 5 : Lancer le test pour vérifier le succès**

Run : `dotnet test ForgeConflict.sln --filter FullyQualifiedName~GpuVendorTests`
Expected : `Passed! - Failed: 0, Passed: 8`.

- [ ] **Step 6 : Commit**

```bash
git add src/ForgeConflict.Core/Inventory tests/ForgeConflict.Tests/Inventory
git commit -m "Add collector contract and PCI-based GPU vendor detection"
```

---

### Task 2 : Réglage registre avec distinction absent / présent

**Files:**
- Create: `src/ForgeConflict.Core/Inventory/Model/RegistrySetting.cs`
- Test: `tests/ForgeConflict.Tests/Inventory/RegistrySettingTests.cs`

- [ ] **Step 1 : Écrire les tests qui échouent**

Create `tests/ForgeConflict.Tests/Inventory/RegistrySettingTests.cs` :

```csharp
using ForgeConflict.Core.Inventory.Model;

namespace ForgeConflict.Tests.Inventory;

public sealed class RegistrySettingTests
{
    [Fact]
    public void Absent_ReportsTheDocumentedDefaultRatherThanZero()
    {
        // TdrDelay absent signifie 2 secondes, pas 0. Lire "absent" comme zero ferait
        // conclure a un TDR desactive, ce qui est faux et dangereux.
        var setting = RegistrySetting.Absent("TdrDelay", effectiveDefault: 2);

        Assert.False(setting.IsPresent);
        Assert.Null(setting.RawValue);
        Assert.Equal(2, setting.EffectiveValue);
        Assert.True(setting.IsDefault);
    }

    [Fact]
    public void Present_WithValueEqualToDefaultIsStillMarkedPresent()
    {
        var setting = RegistrySetting.Present("TdrDelay", value: 2, effectiveDefault: 2);

        Assert.True(setting.IsPresent);
        Assert.Equal(2, setting.RawValue);
        Assert.Equal(2, setting.EffectiveValue);
        Assert.True(setting.IsDefault);
    }

    [Fact]
    public void Present_WithDifferentValueIsNotDefault()
    {
        var setting = RegistrySetting.Present("TdrDelay", value: 8, effectiveDefault: 2);

        Assert.True(setting.IsPresent);
        Assert.Equal(8, setting.EffectiveValue);
        Assert.False(setting.IsDefault);
    }

    [Fact]
    public void Describe_StatesExplicitlyWhetherTheValueWasSet()
    {
        Assert.Contains("non definie", RegistrySetting.Absent("HwSchMode", 2).Describe(), StringComparison.Ordinal);
        Assert.Contains("definie", RegistrySetting.Present("HwSchMode", 1, 2).Describe(), StringComparison.Ordinal);
    }
}
```

- [ ] **Step 2 : Lancer les tests pour vérifier l'échec**

Run : `dotnet test ForgeConflict.sln --filter FullyQualifiedName~RegistrySettingTests`
Expected : échec de compilation, `RegistrySetting` étant introuvable.

- [ ] **Step 3 : Écrire le modèle**

Create `src/ForgeConflict.Core/Inventory/Model/RegistrySetting.cs` :

```csharp
using System.Globalization;

namespace ForgeConflict.Core.Inventory.Model;

/// <summary>
/// Reglage registre lu sur la machine. Une valeur absente n'est PAS zero : elle signifie
/// que Windows applique sa valeur par defaut, souvent differente. Confondre les deux
/// conduirait a diagnostiquer un probleme inexistant, ou pire, a en masquer un reel.
/// </summary>
public sealed record RegistrySetting(
    string Name,
    bool IsPresent,
    object? RawValue,
    object? EffectiveDefault)
{
    /// <summary>Valeur reellement appliquee par Windows : celle lue, ou le defaut si absente.</summary>
    public object? EffectiveValue => IsPresent ? RawValue : EffectiveDefault;

    public bool IsDefault => Equals(EffectiveValue, EffectiveDefault);

    public static RegistrySetting Absent(string name, object? effectiveDefault) =>
        new(name, false, null, effectiveDefault);

    public static RegistrySetting Present(string name, object? value, object? effectiveDefault) =>
        new(name, true, value, effectiveDefault);

    public string Describe()
    {
        var value = Convert.ToString(EffectiveValue, CultureInfo.InvariantCulture) ?? "-";
        return IsPresent
            ? $"{Name} : valeur definie a {value}"
            : $"{Name} : valeur non definie, Windows applique {value}";
    }
}
```

- [ ] **Step 4 : Lancer les tests pour vérifier le succès**

Run : `dotnet test ForgeConflict.sln --filter FullyQualifiedName~RegistrySettingTests`
Expected : `Passed! - Failed: 0, Passed: 4`.

- [ ] **Step 5 : Commit**

```bash
git add src/ForgeConflict.Core/Inventory/Model tests/ForgeConflict.Tests/Inventory
git commit -m "Add registry setting model separating absent from explicitly set"
```

---

### Task 3 : Modèles d'inventaire

**Files:**
- Create: `src/ForgeConflict.Core/Inventory/Model/GpuInfo.cs`
- Create: `src/ForgeConflict.Core/Inventory/Model/DriverPackage.cs`
- Create: `src/ForgeConflict.Core/Inventory/Model/ServiceInfo.cs`
- Create: `src/ForgeConflict.Core/Inventory/Model/ProcessInfo.cs`
- Create: `src/ForgeConflict.Core/Inventory/Model/GraphicsSettings.cs`

- [ ] **Step 1 : Écrire les modèles**

Create `src/ForgeConflict.Core/Inventory/Model/GpuInfo.cs` :

```csharp
namespace ForgeConflict.Core.Inventory.Model;

public sealed record GpuInfo(
    string Name,
    GpuVendor Vendor,
    string DeviceId,
    string? DriverVersion,
    DateTimeOffset? DriverDate,
    string? InfName,
    bool IsSigned,
    long AdapterRamBytes,
    string? Status)
{
    /// <summary>Un pilote d'affichage generique signale un pilote constructeur absent ou casse.</summary>
    public bool IsGenericMicrosoftDriver =>
        Vendor == GpuVendor.Microsoft ||
        (Name?.Contains("Basic Display", StringComparison.OrdinalIgnoreCase) ?? false) ||
        (Name?.Contains("Hyper-V Video", StringComparison.OrdinalIgnoreCase) ?? false);
}
```

Create `src/ForgeConflict.Core/Inventory/Model/DriverPackage.cs` :

```csharp
namespace ForgeConflict.Core.Inventory.Model;

/// <summary>Paquet present dans le magasin de pilotes (DriverStore).</summary>
public sealed record DriverPackage(
    string PublishedName,
    string? OriginalName,
    string? ProviderName,
    string? ClassName,
    string? ClassGuid,
    string? DriverVersion,
    DateTimeOffset? DriverDate,
    string? SignerName)
{
    public bool IsDisplayClass =>
        string.Equals(ClassName, "Display", StringComparison.OrdinalIgnoreCase);

    public bool IsUnsigned => string.IsNullOrWhiteSpace(SignerName);
}
```

Create `src/ForgeConflict.Core/Inventory/Model/ServiceInfo.cs` :

```csharp
namespace ForgeConflict.Core.Inventory.Model;

public sealed record ServiceInfo(
    string Name,
    string? DisplayName,
    string? State,
    string? StartMode,
    string? PathName,
    GpuVendor RelatedVendor);
```

Create `src/ForgeConflict.Core/Inventory/Model/ProcessInfo.cs` :

```csharp
namespace ForgeConflict.Core.Inventory.Model;

public sealed record ProcessInfo(
    int Id,
    string Name,
    string? ExecutablePath,
    string? Company);
```

Create `src/ForgeConflict.Core/Inventory/Model/GraphicsSettings.cs` :

```csharp
namespace ForgeConflict.Core.Inventory.Model;

/// <summary>
/// Reglages graphiques globaux lus sous HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers.
/// Chaque champ porte sa propre distinction absent / defini : voir <see cref="RegistrySetting"/>.
/// </summary>
public sealed record GraphicsSettings(
    RegistrySetting TdrLevel,
    RegistrySetting TdrDelay,
    RegistrySetting TdrDdiDelay,
    RegistrySetting HardwareAcceleratedScheduling,
    RegistrySetting MultiPlaneOverlay);
```

- [ ] **Step 2 : Compiler**

Run : `dotnet build ForgeConflict.sln --nologo`
Expected : `0 Erreur(s)`, `0 Avertissement(s)`.

- [ ] **Step 3 : Commit**

```bash
git add src/ForgeConflict.Core/Inventory/Model
git commit -m "Add inventory model records for GPUs, drivers, services and settings"
```

---

### Task 4 : Collecteur GPU

**Files:**
- Create: `src/ForgeConflict.Core/Inventory/Collectors/GpuCollector.cs`
- Modify: `src/ForgeConflict.Core/ForgeConflict.Core.csproj`
- Test: `tests/ForgeConflict.Tests/Inventory/GpuCollectorTests.cs`

- [ ] **Step 1 : Ajouter la dépendance WMI**

Modify `src/ForgeConflict.Core/ForgeConflict.Core.csproj` — ajouter avant `</Project>` :

```xml
  <ItemGroup>
    <PackageReference Include="System.Management" Version="8.0.0" />
  </ItemGroup>
```

- [ ] **Step 2 : Écrire les tests qui échouent**

Ces tests s'exécutent contre la machine réelle. Ils vérifient le contrat et la robustesse, jamais
un modèle de GPU précis — sinon ils échoueraient sur toute autre machine, y compris la VM.

Create `tests/ForgeConflict.Tests/Inventory/GpuCollectorTests.cs` :

```csharp
using ForgeConflict.Core.Inventory;
using ForgeConflict.Core.Inventory.Collectors;

namespace ForgeConflict.Tests.Inventory;

public sealed class GpuCollectorTests
{
    [Fact]
    public void Collect_ReturnsAtLeastOneAdapterOnAnyWindowsMachine()
    {
        var gpus = new GpuCollector().Collect(CancellationToken.None);

        Assert.NotEmpty(gpus);
        Assert.All(gpus, g => Assert.False(string.IsNullOrWhiteSpace(g.Name)));
    }

    [Fact]
    public void Collect_ResolvesVendorFromDeviceIdWhenPciDeviceIsPresent()
    {
        var gpus = new GpuCollector().Collect(CancellationToken.None);

        foreach (var gpu in gpus.Where(g => g.DeviceId.StartsWith("PCI", StringComparison.OrdinalIgnoreCase)))
        {
            Assert.NotEqual(GpuVendor.Unknown, gpu.Vendor);
        }
    }

    [Fact]
    public void Collect_HonoursCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // L'annulation doit interrompre proprement, jamais laisser un resultat partiel
        // passer pour un inventaire complet. ThrowsAny et non Throws : le type exact
        // varie selon le point d'interruption (OperationCanceledException ou derivee).
        Assert.ThrowsAny<OperationCanceledException>(() => new GpuCollector().Collect(cts.Token));
    }

    [Fact]
    public void Metadata_IsStableAndUsable()
    {
        var collector = new GpuCollector();

        Assert.Equal("gpu", collector.Source);
        Assert.True(collector.Timeout > TimeSpan.Zero);
    }
}
```

- [ ] **Step 3 : Lancer les tests pour vérifier l'échec**

Run : `dotnet test ForgeConflict.sln --filter FullyQualifiedName~GpuCollectorTests`
Expected : échec de compilation, `GpuCollector` étant introuvable.

- [ ] **Step 4 : Écrire le collecteur**

Create `src/ForgeConflict.Core/Inventory/Collectors/GpuCollector.cs` :

```csharp
using System.Globalization;
using System.Management;
using ForgeConflict.Core.Inventory.Model;

namespace ForgeConflict.Core.Inventory.Collectors;

/// <summary>
/// Inventorie les adaptateurs graphiques. Deux sources sont croisees :
/// Win32_VideoController pour l'etat et la memoire, Win32_PnPSignedDriver pour le
/// paquet de pilote et la signature. Toutes deux sont structurees et independantes
/// de la langue du systeme, contrairement a la sortie texte de pnputil.
/// </summary>
public sealed class GpuCollector : ICollector<IReadOnlyList<GpuInfo>>
{
    public string Source => "gpu";

    public TimeSpan Timeout => TimeSpan.FromSeconds(20);

    public IReadOnlyList<GpuInfo> Collect(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var drivers = ReadSignedDrivers(cancellationToken);
        var result = new List<GpuInfo>();

        using var searcher = new ManagementObjectSearcher(
            "SELECT Name, PNPDeviceID, DriverVersion, DriverDate, AdapterRAM, Status FROM Win32_VideoController");
        using var controllers = searcher.Get();

        foreach (var item in controllers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var controller = item;

            var deviceId = Text(controller["PNPDeviceID"]) ?? string.Empty;
            drivers.TryGetValue(deviceId, out var driver);

            result.Add(new GpuInfo(
                Name: Text(controller["Name"]) ?? "Adaptateur inconnu",
                Vendor: GpuVendorReader.FromDeviceId(deviceId),
                DeviceId: deviceId,
                DriverVersion: driver?.Version ?? Text(controller["DriverVersion"]),
                DriverDate: driver?.Date ?? ParseCimDate(Text(controller["DriverDate"])),
                InfName: driver?.InfName,
                IsSigned: driver?.IsSigned ?? false,
                AdapterRamBytes: ToLong(controller["AdapterRAM"]),
                Status: Text(controller["Status"])));
        }

        return result;
    }

    private static Dictionary<string, DriverRow> ReadSignedDrivers(CancellationToken cancellationToken)
    {
        var map = new Dictionary<string, DriverRow>(StringComparer.OrdinalIgnoreCase);

        using var searcher = new ManagementObjectSearcher(
            "SELECT DeviceID, DriverVersion, DriverDate, InfName, IsSigned FROM Win32_PnPSignedDriver WHERE DeviceClass='DISPLAY'");
        using var rows = searcher.Get();

        foreach (var item in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var row = item;

            var id = Text(row["DeviceID"]);
            if (string.IsNullOrWhiteSpace(id)) continue;

            map[id] = new DriverRow(
                Text(row["DriverVersion"]),
                ParseCimDate(Text(row["DriverDate"])),
                Text(row["InfName"]),
                row["IsSigned"] is bool signed && signed);
        }

        return map;
    }

    private static string? Text(object? value) => value?.ToString();

    private static long ToLong(object? value) =>
        value is null ? 0L : Convert.ToInt64(value, CultureInfo.InvariantCulture);

    /// <summary>
    /// WMI rend les dates au format CIM (yyyyMMddHHmmss.ffffff+UUU) via WQL brut.
    /// ManagementObjectSearcher peut aussi rendre un DateTime deja converti selon
    /// le fournisseur : les deux formes sont acceptees.
    /// </summary>
    private static DateTimeOffset? ParseCimDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        if (DateTimeOffset.TryParse(raw, CultureInfo.CurrentCulture, DateTimeStyles.None, out var parsed))
        {
            return parsed;
        }

        if (raw.Length >= 14 &&
            DateTime.TryParseExact(raw[..14], "yyyyMMddHHmmss", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var cim))
        {
            return new DateTimeOffset(cim, TimeSpan.Zero);
        }

        return null;
    }

    private sealed record DriverRow(string? Version, DateTimeOffset? Date, string? InfName, bool IsSigned);
}
```

- [ ] **Step 5 : Lancer les tests pour vérifier le succès**

Run : `dotnet test ForgeConflict.sln --filter FullyQualifiedName~GpuCollectorTests`
Expected : `Passed! - Failed: 0, Passed: 4`.

- [ ] **Step 6 : Commit**

```bash
git add src/ForgeConflict.Core tests/ForgeConflict.Tests/Inventory
git commit -m "Add GPU collector cross-referencing video controllers and signed drivers"
```

---

### Task 5 : Collecteur du magasin de pilotes

**Files:**
- Create: `src/ForgeConflict.Core/Inventory/Collectors/DriverStoreCollector.cs`
- Test: `tests/ForgeConflict.Tests/Inventory/DriverStoreCollectorTests.cs`

- [ ] **Step 1 : Écrire les tests qui échouent**

Create `tests/ForgeConflict.Tests/Inventory/DriverStoreCollectorTests.cs` :

```csharp
using ForgeConflict.Core.Inventory.Collectors;

namespace ForgeConflict.Tests.Inventory;

public sealed class DriverStoreCollectorTests
{
    [Fact]
    public void ParseXml_ReadsEnglishElementNamesWhateverTheSystemLanguage()
    {
        // pnputil rend un XML dont les balises sont en anglais meme sur un Windows
        // francais. C'est precisement pourquoi la sortie texte est ecartee.
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <PnpUtil>
              <Driver>
                <PublishedName>oem113.inf</PublishedName>
                <OriginalName>nvblt.inf</OriginalName>
                <ProviderName>NVIDIA</ProviderName>
                <ClassName>Display</ClassName>
                <ClassGuid>{4d36e968-e325-11ce-bfc1-08002be10318}</ClassGuid>
                <DriverVersion>12/30/2025 32.0.15.9174</DriverVersion>
                <SignerName>Microsoft Windows Hardware Compatibility Publisher</SignerName>
              </Driver>
            </PnpUtil>
            """;

        var packages = DriverStoreCollector.ParseXml(xml);

        var package = Assert.Single(packages);
        Assert.Equal("oem113.inf", package.PublishedName);
        Assert.Equal("NVIDIA", package.ProviderName);
        Assert.True(package.IsDisplayClass);
        Assert.Equal("32.0.15.9174", package.DriverVersion);
        Assert.Equal(new DateTimeOffset(2025, 12, 30, 0, 0, 0, TimeSpan.Zero), package.DriverDate);
        Assert.False(package.IsUnsigned);
    }

    [Fact]
    public void ParseXml_FlagsPackagesWithoutSigner()
    {
        const string xml = """
            <PnpUtil>
              <Driver>
                <PublishedName>oem9.inf</PublishedName>
                <ClassName>Display</ClassName>
                <DriverVersion>01/02/2020 1.0.0.1</DriverVersion>
              </Driver>
            </PnpUtil>
            """;

        var package = Assert.Single(DriverStoreCollector.ParseXml(xml));
        Assert.True(package.IsUnsigned);
    }

    [Fact]
    public void ParseXml_ReturnsEmptyOnMalformedInputInsteadOfThrowing()
    {
        // Un XML tronque ne doit pas faire tomber le scan entier.
        Assert.Empty(DriverStoreCollector.ParseXml("<PnpUtil><Driver>"));
        Assert.Empty(DriverStoreCollector.ParseXml(""));
    }

    [Fact]
    public void Collect_ReturnsDisplayPackagesFromTheRealMachine()
    {
        var packages = new DriverStoreCollector().Collect(CancellationToken.None);

        Assert.All(packages, p => Assert.False(string.IsNullOrWhiteSpace(p.PublishedName)));
    }
}
```

- [ ] **Step 2 : Lancer les tests pour vérifier l'échec**

Run : `dotnet test ForgeConflict.sln --filter FullyQualifiedName~DriverStoreCollectorTests`
Expected : échec de compilation, `DriverStoreCollector` étant introuvable.

- [ ] **Step 3 : Écrire le collecteur**

Create `src/ForgeConflict.Core/Inventory/Collectors/DriverStoreCollector.cs` :

```csharp
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Xml.Linq;
using ForgeConflict.Core.Inventory.Model;

namespace ForgeConflict.Core.Inventory.Collectors;

/// <summary>
/// Enumere les paquets de pilote d'affichage du magasin. La sortie XML de pnputil est
/// utilisee et non sa sortie texte : cette derniere est traduite dans la langue du
/// systeme et son analyse casserait hors d'un Windows francais.
/// </summary>
public sealed class DriverStoreCollector : ICollector<IReadOnlyList<DriverPackage>>
{
    private const string DisplayClassGuid = "{4d36e968-e325-11ce-bfc1-08002be10318}";

    public string Source => "magasin-pilotes";

    public TimeSpan Timeout => TimeSpan.FromSeconds(45);

    public IReadOnlyList<DriverPackage> Collect(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ParseXml(RunPnpUtil(cancellationToken));
    }

    private static string RunPnpUtil(CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo
        {
            FileName = "pnputil.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        info.ArgumentList.Add("/enum-drivers");
        info.ArgumentList.Add("/class");
        info.ArgumentList.Add(DisplayClassGuid);
        info.ArgumentList.Add("/format");
        info.ArgumentList.Add("xml");

        using var process = Process.Start(info);
        if (process is null) return string.Empty;

        var output = process.StandardOutput.ReadToEnd();

        // Le processus est tue s'il ne rend pas la main : un pnputil bloque ne doit pas
        // immobiliser le scan.
        if (!process.WaitForExit(30_000))
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            return string.Empty;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return output;
    }

    /// <summary>Analyse la sortie XML. Un document illisible rend une liste vide, jamais une exception.</summary>
    public static IReadOnlyList<DriverPackage> ParseXml(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return [];

        XDocument document;
        try
        {
            document = XDocument.Parse(xml);
        }
        catch (System.Xml.XmlException)
        {
            return [];
        }

        var packages = new List<DriverPackage>();

        foreach (var node in document.Descendants("Driver"))
        {
            var published = Value(node, "PublishedName");
            if (string.IsNullOrWhiteSpace(published)) continue;

            var (date, version) = SplitDriverVersion(Value(node, "DriverVersion"));

            packages.Add(new DriverPackage(
                PublishedName: published,
                OriginalName: Value(node, "OriginalName"),
                ProviderName: Value(node, "ProviderName"),
                ClassName: Value(node, "ClassName"),
                ClassGuid: Value(node, "ClassGuid"),
                DriverVersion: version,
                DriverDate: date,
                SignerName: Value(node, "SignerName")));
        }

        return packages;
    }

    private static string? Value(XElement node, string name) => node.Element(name)?.Value?.Trim();

    /// <summary>
    /// pnputil concatene date et version dans un seul champ, ex. "12/30/2025 32.0.15.9174",
    /// et la date y est toujours au format americain quelle que soit la langue.
    /// </summary>
    private static (DateTimeOffset? Date, string? Version) SplitDriverVersion(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return (null, null);

        var parts = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2) return (null, raw);

        DateTimeOffset? date = DateTime.TryParseExact(
            parts[0], "MM/dd/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? new DateTimeOffset(parsed, TimeSpan.Zero)
            : null;

        return (date, parts[1]);
    }
}
```

- [ ] **Step 4 : Lancer les tests pour vérifier le succès**

Run : `dotnet test ForgeConflict.sln --filter FullyQualifiedName~DriverStoreCollectorTests`
Expected : `Passed! - Failed: 0, Passed: 4`.

- [ ] **Step 5 : Commit**

```bash
git add src/ForgeConflict.Core/Inventory/Collectors tests/ForgeConflict.Tests/Inventory
git commit -m "Add driver store collector parsing pnputil XML instead of localized text"
```

---

### Task 6 : Collecteur de réglages graphiques

**Files:**
- Create: `src/ForgeConflict.Core/Inventory/Collectors/GraphicsSettingsCollector.cs`
- Test: `tests/ForgeConflict.Tests/Inventory/GraphicsSettingsCollectorTests.cs`

- [ ] **Step 1 : Écrire les tests qui échouent**

Create `tests/ForgeConflict.Tests/Inventory/GraphicsSettingsCollectorTests.cs` :

```csharp
using ForgeConflict.Core.Inventory.Collectors;

namespace ForgeConflict.Tests.Inventory;

public sealed class GraphicsSettingsCollectorTests
{
    [Fact]
    public void Collect_NeverReportsAbsentKeysAsZero()
    {
        // Sur une machine par defaut ces cles n'existent pas. Les lire comme zero
        // ferait croire a un TDR desactive et a une planification GPU eteinte.
        var settings = new GraphicsSettingsCollector().Collect(CancellationToken.None);

        foreach (var setting in new[]
                 {
                     settings.TdrLevel, settings.TdrDelay, settings.TdrDdiDelay,
                     settings.HardwareAcceleratedScheduling, settings.MultiPlaneOverlay,
                 })
        {
            if (!setting.IsPresent)
            {
                Assert.NotNull(setting.EffectiveValue);
                Assert.True(setting.IsDefault);
            }
        }
    }

    [Fact]
    public void Collect_AppliesDocumentedWindowsDefaults()
    {
        var settings = new GraphicsSettingsCollector().Collect(CancellationToken.None);

        // Defauts Windows documentes : TdrLevel=3 (recuperation), TdrDelay=2 s, TdrDdiDelay=5 s.
        Assert.Equal(3, settings.TdrLevel.EffectiveDefault);
        Assert.Equal(2, settings.TdrDelay.EffectiveDefault);
        Assert.Equal(5, settings.TdrDdiDelay.EffectiveDefault);
    }

    [Fact]
    public void Collect_NamesEverySettingForDisplay()
    {
        var settings = new GraphicsSettingsCollector().Collect(CancellationToken.None);

        Assert.Equal("TdrDelay", settings.TdrDelay.Name);
        Assert.Equal("HwSchMode", settings.HardwareAcceleratedScheduling.Name);
        Assert.Contains("Tdr", settings.TdrLevel.Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public void Metadata_IsStableAndUsable()
    {
        var collector = new GraphicsSettingsCollector();

        Assert.Equal("reglages-graphiques", collector.Source);
        Assert.True(collector.Timeout > TimeSpan.Zero);
    }
}
```

- [ ] **Step 2 : Lancer les tests pour vérifier l'échec**

Run : `dotnet test ForgeConflict.sln --filter FullyQualifiedName~GraphicsSettingsCollectorTests`
Expected : échec de compilation, `GraphicsSettingsCollector` étant introuvable.

- [ ] **Step 3 : Écrire le collecteur**

Create `src/ForgeConflict.Core/Inventory/Collectors/GraphicsSettingsCollector.cs` :

```csharp
using ForgeConflict.Core.Inventory.Model;
using Microsoft.Win32;

namespace ForgeConflict.Core.Inventory.Collectors;

/// <summary>
/// Lit les reglages graphiques globaux. Ces cles sont le plus souvent ABSENTES sur une
/// machine saine : Windows applique alors ses valeurs par defaut. Chaque reglage porte
/// donc son defaut documente, pour qu'une valeur manquante ne soit jamais confondue
/// avec un zero.
/// </summary>
public sealed class GraphicsSettingsCollector : ICollector<GraphicsSettings>
{
    private const string KeyPath = @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers";

    public string Source => "reglages-graphiques";

    public TimeSpan Timeout => TimeSpan.FromSeconds(10);

    public GraphicsSettings Collect(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var key = Registry.LocalMachine.OpenSubKey(KeyPath, writable: false);

        return new GraphicsSettings(
            // Recuperation activee par defaut.
            TdrLevel: Read(key, "TdrLevel", 3),
            // Deux secondes avant de considerer le GPU bloque.
            TdrDelay: Read(key, "TdrDelay", 2),
            // Cinq secondes pour un appel DDI.
            TdrDdiDelay: Read(key, "TdrDdiDelay", 5),
            // Planification GPU acceleree : 2 = disponible mais eteinte.
            HardwareAcceleratedScheduling: Read(key, "HwSchMode", 2),
            // Multi-Plane Overlay : actif tant qu'il n'est pas explicitement desactive.
            MultiPlaneOverlay: Read(key, "OverlayTestMode", 0));
    }

    private static RegistrySetting Read(RegistryKey? key, string name, object effectiveDefault)
    {
        var value = key?.GetValue(name);
        return value is null
            ? RegistrySetting.Absent(name, effectiveDefault)
            : RegistrySetting.Present(name, value, effectiveDefault);
    }
}
```

- [ ] **Step 4 : Lancer les tests pour vérifier le succès**

Run : `dotnet test ForgeConflict.sln --filter FullyQualifiedName~GraphicsSettingsCollectorTests`
Expected : `Passed! - Failed: 0, Passed: 4`.

- [ ] **Step 5 : Commit**

```bash
git add src/ForgeConflict.Core/Inventory/Collectors tests/ForgeConflict.Tests/Inventory
git commit -m "Add graphics settings collector carrying documented Windows defaults"
```

---

### Task 7 : Collecteur de services

**Files:**
- Create: `src/ForgeConflict.Core/Inventory/Collectors/ServiceCollector.cs`
- Test: `tests/ForgeConflict.Tests/Inventory/ServiceCollectorTests.cs`

- [ ] **Step 1 : Écrire les tests qui échouent**

Create `tests/ForgeConflict.Tests/Inventory/ServiceCollectorTests.cs` :

```csharp
using ForgeConflict.Core.Inventory;
using ForgeConflict.Core.Inventory.Collectors;

namespace ForgeConflict.Tests.Inventory;

public sealed class ServiceCollectorTests
{
    [Theory]
    [InlineData("NVDisplay.ContainerLocalSystem", GpuVendor.Nvidia)]
    [InlineData("nvagent", GpuVendor.Nvidia)]
    [InlineData("AMD External Events Utility", GpuVendor.Amd)]
    [InlineData("amdwddmg", GpuVendor.Amd)]
    [InlineData("igfxCUIService2.0.0.0", GpuVendor.Intel)]
    [InlineData("IntelGraphicsSoftwareService", GpuVendor.Intel)]
    [InlineData("Spooler", GpuVendor.Unknown)]
    public void ClassifyVendor_MapsKnownGraphicsServices(string name, GpuVendor expected) =>
        Assert.Equal(expected, ServiceCollector.ClassifyVendor(name));

    [Fact]
    public void ClassifyVendor_DoesNotMatchOnAmbiguousSubstrings()
    {
        // "nv" seul est trop court : il apparait dans des noms sans rapport.
        Assert.Equal(GpuVendor.Unknown, ServiceCollector.ClassifyVendor("EnvironmentService"));
        Assert.Equal(GpuVendor.Unknown, ServiceCollector.ClassifyVendor("WinDefend"));
    }

    [Fact]
    public void Collect_ReturnsOnlyGraphicsRelatedServices()
    {
        var services = new ServiceCollector().Collect(CancellationToken.None);

        Assert.All(services, s => Assert.NotEqual(GpuVendor.Unknown, s.RelatedVendor));
        Assert.All(services, s => Assert.False(string.IsNullOrWhiteSpace(s.Name)));
    }

    [Fact]
    public void Metadata_IsStableAndUsable()
    {
        var collector = new ServiceCollector();

        Assert.Equal("services", collector.Source);
        Assert.True(collector.Timeout > TimeSpan.Zero);
    }
}
```

- [ ] **Step 2 : Lancer les tests pour vérifier l'échec**

Run : `dotnet test ForgeConflict.sln --filter FullyQualifiedName~ServiceCollectorTests`
Expected : échec de compilation, `ServiceCollector` étant introuvable.

- [ ] **Step 3 : Écrire le collecteur**

Create `src/ForgeConflict.Core/Inventory/Collectors/ServiceCollector.cs` :

```csharp
using System.Management;
using ForgeConflict.Core.Inventory.Model;

namespace ForgeConflict.Core.Inventory.Collectors;

/// <summary>
/// Recense les services lies a un pilote graphique. Les motifs sont assez longs pour
/// eviter les faux positifs : "nv" seul apparait dans des noms sans aucun rapport.
/// </summary>
public sealed class ServiceCollector : ICollector<IReadOnlyList<ServiceInfo>>
{
    private static readonly (string Pattern, GpuVendor Vendor)[] Patterns =
    [
        ("nvdisplay", GpuVendor.Nvidia),
        ("nvcontainer", GpuVendor.Nvidia),
        ("nvagent", GpuVendor.Nvidia),
        ("nvlddmkm", GpuVendor.Nvidia),
        ("nvsvc", GpuVendor.Nvidia),
        ("nvidia", GpuVendor.Nvidia),
        ("amd external events", GpuVendor.Amd),
        ("amdwddm", GpuVendor.Amd),
        ("amdkmdap", GpuVendor.Amd),
        ("amdlog", GpuVendor.Amd),
        ("radeon", GpuVendor.Amd),
        ("igfx", GpuVendor.Intel),
        ("intelgraphics", GpuVendor.Intel),
        ("igcc", GpuVendor.Intel),
        ("inteldisplay", GpuVendor.Intel),
    ];

    public string Source => "services";

    public TimeSpan Timeout => TimeSpan.FromSeconds(20);

    public static GpuVendor ClassifyVendor(string? serviceName)
    {
        if (string.IsNullOrWhiteSpace(serviceName)) return GpuVendor.Unknown;

        var normalized = serviceName.Replace(" ", string.Empty).ToLowerInvariant();

        foreach (var (pattern, vendor) in Patterns)
        {
            if (normalized.Contains(pattern.Replace(" ", string.Empty), StringComparison.Ordinal))
            {
                return vendor;
            }
        }

        return GpuVendor.Unknown;
    }

    public IReadOnlyList<ServiceInfo> Collect(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var services = new List<ServiceInfo>();

        using var searcher = new ManagementObjectSearcher(
            "SELECT Name, DisplayName, State, StartMode, PathName FROM Win32_Service");
        using var rows = searcher.Get();

        foreach (var item in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var row = item;

            var name = row["Name"]?.ToString();
            var display = row["DisplayName"]?.ToString();

            // Le nom court et le nom affiche sont tous deux examines : certains
            // services constructeurs n'exposent le fournisseur que dans l'un des deux.
            var vendor = ClassifyVendor(name);
            if (vendor == GpuVendor.Unknown) vendor = ClassifyVendor(display);
            if (vendor == GpuVendor.Unknown) continue;

            services.Add(new ServiceInfo(
                Name: name ?? string.Empty,
                DisplayName: display,
                State: row["State"]?.ToString(),
                StartMode: row["StartMode"]?.ToString(),
                PathName: row["PathName"]?.ToString(),
                RelatedVendor: vendor));
        }

        return services;
    }
}
```

- [ ] **Step 4 : Lancer les tests pour vérifier le succès**

Run : `dotnet test ForgeConflict.sln --filter FullyQualifiedName~ServiceCollectorTests`
Expected : `Passed! - Failed: 0, Passed: 10` (7 cas de théorie plus 3 faits).

- [ ] **Step 5 : Commit**

```bash
git add src/ForgeConflict.Core/Inventory/Collectors tests/ForgeConflict.Tests/Inventory
git commit -m "Add graphics service collector with false-positive resistant matching"
```

---

### Task 8 : Collecteur de processus à risque

**Files:**
- Create: `src/ForgeConflict.Core/Inventory/Collectors/OverlayProcessCollector.cs`
- Test: `tests/ForgeConflict.Tests/Inventory/OverlayProcessCollectorTests.cs`

- [ ] **Step 1 : Écrire les tests qui échouent**

Create `tests/ForgeConflict.Tests/Inventory/OverlayProcessCollectorTests.cs` :

```csharp
using ForgeConflict.Core.Inventory.Collectors;

namespace ForgeConflict.Tests.Inventory;

public sealed class OverlayProcessCollectorTests
{
    [Theory]
    [InlineData("Discord", true)]
    [InlineData("RTSS", true)]
    [InlineData("MSIAfterburner", true)]
    [InlineData("obs64", true)]
    [InlineData("steamwebhelper", true)]
    [InlineData("notepad", false)]
    [InlineData("explorer", false)]
    public void IsKnownOverlay_RecognisesRenderingHooks(string processName, bool expected) =>
        Assert.Equal(expected, OverlayProcessCollector.IsKnownOverlay(processName));

    [Fact]
    public void IsKnownOverlay_IgnoresCaseAndExtension()
    {
        Assert.True(OverlayProcessCollector.IsKnownOverlay("DISCORD.EXE"));
        Assert.True(OverlayProcessCollector.IsKnownOverlay("obs64.exe"));
    }

    [Fact]
    public void IsKnownOverlay_TreatsNullAsNotAnOverlay() =>
        Assert.False(OverlayProcessCollector.IsKnownOverlay(null));

    [Fact]
    public void Collect_NeverThrowsOnProcessesThatVanishOrDenyAccess()
    {
        // Un processus peut disparaitre entre l'enumeration et la lecture de ses
        // proprietes, et certains refusent l'acces meme en administrateur.
        var processes = new OverlayProcessCollector().Collect(CancellationToken.None);

        Assert.All(processes, p => Assert.False(string.IsNullOrWhiteSpace(p.Name)));
    }

    [Fact]
    public void Metadata_IsStableAndUsable()
    {
        var collector = new OverlayProcessCollector();

        Assert.Equal("processus-overlay", collector.Source);
        Assert.True(collector.Timeout > TimeSpan.Zero);
    }
}
```

- [ ] **Step 2 : Lancer les tests pour vérifier l'échec**

Run : `dotnet test ForgeConflict.sln --filter FullyQualifiedName~OverlayProcessCollectorTests`
Expected : échec de compilation, `OverlayProcessCollector` étant introuvable.

- [ ] **Step 3 : Écrire le collecteur**

Create `src/ForgeConflict.Core/Inventory/Collectors/OverlayProcessCollector.cs` :

```csharp
using System.Diagnostics;
using ForgeConflict.Core.Inventory.Model;

namespace ForgeConflict.Core.Inventory.Collectors;

/// <summary>
/// Repere les processus connus pour s'accrocher au rendu : overlays, injecteurs,
/// enregistreurs. C'est la premiere cause de plantage graphique applicatif.
/// Le collecteur constate leur presence, il ne juge pas.
/// </summary>
public sealed class OverlayProcessCollector : ICollector<IReadOnlyList<ProcessInfo>>
{
    private static readonly string[] KnownOverlays =
    [
        "discord", "rtss", "rivatuner", "msiafterburner", "obs64", "obs32",
        "steamwebhelper", "gameoverlayui", "nvcontainer", "nvidia app",
        "medal", "razer synapse", "overwolf", "epicgameslauncher",
        "eadesktop", "ubisoftconnect", "reshade", "specialk", "losslessscaling",
        "xsplit", "streamlabs", "bandicam", "fraps",
    ];

    public string Source => "processus-overlay";

    public TimeSpan Timeout => TimeSpan.FromSeconds(20);

    public static bool IsKnownOverlay(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return false;

        var normalized = processName
            .Replace(".exe", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(" ", string.Empty)
            .ToLowerInvariant();

        return KnownOverlays.Any(o =>
            normalized.Contains(o.Replace(" ", string.Empty), StringComparison.Ordinal));
    }

    public IReadOnlyList<ProcessInfo> Collect(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var found = new List<ProcessInfo>();

        foreach (var process in Process.GetProcesses())
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (!IsKnownOverlay(process.ProcessName)) continue;

                string? path = null;
                string? company = null;
                try
                {
                    // Un processus protege refuse l'acces a son module principal, meme
                    // en administrateur. On garde alors le nom seul plutot que rien.
                    path = process.MainModule?.FileName;
                    company = process.MainModule?.FileVersionInfo.CompanyName;
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                }

                found.Add(new ProcessInfo(process.Id, process.ProcessName, path, company));
            }
            catch (InvalidOperationException)
            {
                // Le processus s'est termine entre l'enumeration et la lecture.
            }
            finally
            {
                process.Dispose();
            }
        }

        return found;
    }
}
```

- [ ] **Step 4 : Lancer les tests pour vérifier le succès**

Run : `dotnet test ForgeConflict.sln --filter FullyQualifiedName~OverlayProcessCollectorTests`
Expected : `Passed! - Failed: 0, Passed: 11`.

- [ ] **Step 5 : Commit**

```bash
git add src/ForgeConflict.Core/Inventory/Collectors tests/ForgeConflict.Tests/Inventory
git commit -m "Add overlay process collector tolerating vanishing and protected processes"
```

---

### Task 9 : Instantané système et orchestration

**Files:**
- Create: `src/ForgeConflict.Core/Inventory/SystemSnapshot.cs`
- Create: `src/ForgeConflict.Core/Inventory/InventoryScanner.cs`
- Test: `tests/ForgeConflict.Tests/Inventory/InventoryScannerTests.cs`

- [ ] **Step 1 : Écrire les tests qui échouent**

Create `tests/ForgeConflict.Tests/Inventory/InventoryScannerTests.cs` :

```csharp
using ForgeConflict.Core.Collection;
using ForgeConflict.Core.Inventory;

namespace ForgeConflict.Tests.Inventory;

public sealed class InventoryScannerTests
{
    [Fact]
    public async Task ScanAsync_ProducesAFragmentForEverySource()
    {
        var scanner = new InventoryScanner(new IsolatedRunner(new TestLog()));

        var snapshot = await scanner.ScanAsync();

        Assert.NotNull(snapshot.Gpus);
        Assert.NotNull(snapshot.DriverPackages);
        Assert.NotNull(snapshot.Services);
        Assert.NotNull(snapshot.GraphicsSettings);
        Assert.NotNull(snapshot.OverlayProcesses);
        Assert.Equal(5, snapshot.AllFragments.Count);
    }

    [Fact]
    public async Task ScanAsync_RecordsMachineContext()
    {
        var scanner = new InventoryScanner(new IsolatedRunner(new TestLog()));

        var snapshot = await scanner.ScanAsync();

        Assert.False(string.IsNullOrWhiteSpace(snapshot.MachineName));
        Assert.False(string.IsNullOrWhiteSpace(snapshot.OsVersion));
        Assert.True(snapshot.CapturedAt <= DateTimeOffset.Now);
    }

    [Fact]
    public async Task ScanAsync_ReportsFailedSourcesWithoutAbortingTheScan()
    {
        var scanner = new InventoryScanner(new IsolatedRunner(new TestLog()));

        var snapshot = await scanner.ScanAsync();

        // Certaines sources peuvent echouer selon la machine : ce qui compte est
        // qu'un echec soit visible et n'empeche pas les autres d'aboutir.
        Assert.All(snapshot.AllFragments, f => Assert.False(string.IsNullOrWhiteSpace(f.Source)));
        Assert.Contains(snapshot.AllFragments, f => f.Status == FragmentStatus.Ok);
    }

    [Fact]
    public async Task ScanAsync_ExposesUnusableSourcesForDisplay()
    {
        var scanner = new InventoryScanner(new IsolatedRunner(new TestLog()));

        var snapshot = await scanner.ScanAsync();

        // L'interface doit pouvoir dire ce qui manque plutot que de presenter un
        // diagnostic partiel comme s'il etait complet.
        Assert.Equal(
            snapshot.AllFragments.Count(f => !f.IsUsable),
            snapshot.UnusableSources.Count);
    }

    [Fact]
    public async Task ScanAsync_RunsSourcesConcurrently()
    {
        var scanner = new InventoryScanner(new IsolatedRunner(new TestLog()));
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        await scanner.ScanAsync();
        stopwatch.Stop();

        // La somme des delais depasse deux minutes. Une execution sequentielle qui
        // atteindrait ses limites serait bien plus lente que cette borne.
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(60),
            $"le scan a pris {stopwatch.Elapsed}, les sources ne semblent pas paralleles");
    }
}
```

- [ ] **Step 2 : Lancer les tests pour vérifier l'échec**

Run : `dotnet test ForgeConflict.sln --filter FullyQualifiedName~InventoryScannerTests`
Expected : échec de compilation, `InventoryScanner` étant introuvable.

- [ ] **Step 3 : Écrire l'instantané**

Create `src/ForgeConflict.Core/Inventory/SystemSnapshot.cs` :

```csharp
using ForgeConflict.Core.Collection;
using ForgeConflict.Core.Inventory.Model;

namespace ForgeConflict.Core.Inventory;

/// <summary>
/// Etat graphique de la machine a un instant donne. Chaque source est portee par un
/// <see cref="Fragment{T}"/> : une source defaillante reste visible en tant que telle,
/// pour qu'un diagnostic incomplet ne puisse jamais passer pour un diagnostic complet.
/// </summary>
public sealed record SystemSnapshot(
    DateTimeOffset CapturedAt,
    string MachineName,
    string OsVersion,
    Fragment<IReadOnlyList<GpuInfo>> Gpus,
    Fragment<IReadOnlyList<DriverPackage>> DriverPackages,
    Fragment<IReadOnlyList<ServiceInfo>> Services,
    Fragment<GraphicsSettings> GraphicsSettings,
    Fragment<IReadOnlyList<ProcessInfo>> OverlayProcesses)
{
    public IReadOnlyList<IFragment> AllFragments =>
    [
        Gpus, DriverPackages, Services, GraphicsSettings, OverlayProcesses,
    ];

    /// <summary>Sources dont le resultat est inexploitable, a signaler explicitement.</summary>
    public IReadOnlyList<IFragment> UnusableSources =>
        AllFragments.Where(f => !f.IsUsable).ToList();

    public bool IsComplete => UnusableSources.Count == 0;
}
```

- [ ] **Step 4 : Exposer une vue non générique du fragment**

`AllFragments` mélange des `Fragment<T>` de types différents. Une interface non générique permet
de les manipuler ensemble sans réflexion.

Modify `src/ForgeConflict.Core/Collection/Fragment.cs` — remplacer tout le contenu :

```csharp
namespace ForgeConflict.Core.Collection;

/// <summary>Vue non generique d'un fragment, pour presenter des sources heterogenes ensemble.</summary>
public interface IFragment
{
    string Source { get; }
    FragmentStatus Status { get; }
    string? Error { get; }
    TimeSpan Duration { get; }
    bool IsUsable { get; }
}

/// <summary>
/// Resultat d'un collecteur. Un fragment inexploitable n'interrompt jamais le scan :
/// il est presente tel quel a l'utilisateur pour qu'aucun diagnostic partiel ne passe
/// pour un diagnostic complet.
/// </summary>
public sealed record Fragment<T>(
    string Source,
    FragmentStatus Status,
    T? Value,
    string? Error,
    TimeSpan Duration) : IFragment
{
    public bool IsUsable => Status == FragmentStatus.Ok;

    public static Fragment<T> Ok(string source, T value, TimeSpan duration) =>
        new(source, FragmentStatus.Ok, value, null, duration);

    public static Fragment<T> Failed(string source, string error, TimeSpan duration) =>
        new(source, FragmentStatus.Failed, default, error, duration);

    public static Fragment<T> TimedOut(string source, TimeSpan duration) =>
        new(source, FragmentStatus.TimedOut, default, null, duration);
}
```

- [ ] **Step 5 : Écrire l'orchestrateur**

Create `src/ForgeConflict.Core/Inventory/InventoryScanner.cs` :

```csharp
using ForgeConflict.Core.Collection;
using ForgeConflict.Core.Inventory.Collectors;
using ForgeConflict.Core.Inventory.Model;

namespace ForgeConflict.Core.Inventory;

/// <summary>
/// Execute toutes les sources d'inventaire en parallele et agrege le resultat.
/// Chaque source passe par l'executeur isole : ni une exception ni un blocage ne
/// peuvent interrompre le scan.
/// </summary>
public sealed class InventoryScanner
{
    private readonly IIsolatedRunner _runner;
    private readonly GpuCollector _gpu = new();
    private readonly DriverStoreCollector _driverStore = new();
    private readonly ServiceCollector _services = new();
    private readonly GraphicsSettingsCollector _settings = new();
    private readonly OverlayProcessCollector _overlays = new();

    public InventoryScanner(IIsolatedRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
    }

    public async Task<SystemSnapshot> ScanAsync(CancellationToken cancellationToken = default)
    {
        var gpus = Run<IReadOnlyList<GpuInfo>>(_gpu, cancellationToken);
        var packages = Run<IReadOnlyList<DriverPackage>>(_driverStore, cancellationToken);
        var services = Run<IReadOnlyList<ServiceInfo>>(_services, cancellationToken);
        var settings = Run<GraphicsSettings>(_settings, cancellationToken);
        var overlays = Run<IReadOnlyList<ProcessInfo>>(_overlays, cancellationToken);

        await Task.WhenAll(gpus, packages, services, settings, overlays).ConfigureAwait(false);

        return new SystemSnapshot(
            CapturedAt: DateTimeOffset.Now,
            MachineName: Environment.MachineName,
            OsVersion: Environment.OSVersion.VersionString,
            Gpus: gpus.Result,
            DriverPackages: packages.Result,
            Services: services.Result,
            GraphicsSettings: settings.Result,
            OverlayProcesses: overlays.Result);
    }

    private Task<Fragment<T>> Run<T>(ICollector<T> collector, CancellationToken cancellationToken) =>
        _runner.RunAsync(collector.Source, collector.Collect, collector.Timeout, cancellationToken);
}
```

- [ ] **Step 6 : Lancer les tests pour vérifier le succès**

Run : `dotnet test ForgeConflict.sln --filter FullyQualifiedName~InventoryScannerTests`
Expected : `Passed! - Failed: 0, Passed: 5`.

- [ ] **Step 7 : Lancer toute la suite pour vérifier l'absence de régression**

Run : `dotnet test ForgeConflict.sln --nologo`
Expected : tous les tests du lot 1A passent toujours, l'ajout de `IFragment` n'ayant rien cassé.

- [ ] **Step 8 : Commit**

```bash
git add src/ForgeConflict.Core tests/ForgeConflict.Tests
git commit -m "Add system snapshot and parallel inventory scanner"
```

---

### Task 10 : Sérialisation de l'instantané et affichage

**Files:**
- Modify: `src/ForgeConflict.App/MainWindow.xaml`
- Modify: `src/ForgeConflict.App/MainWindow.xaml.cs`
- Modify: `Directory.Build.props`
- Create: `src/ForgeConflict.Core/Inventory/SnapshotReport.cs`
- Test: `tests/ForgeConflict.Tests/Inventory/SnapshotReportTests.cs`

- [ ] **Step 1 : Écrire les tests qui échouent**

Create `tests/ForgeConflict.Tests/Inventory/SnapshotReportTests.cs` :

```csharp
using ForgeConflict.Core.Collection;
using ForgeConflict.Core.Inventory;

namespace ForgeConflict.Tests.Inventory;

public sealed class SnapshotReportTests
{
    [Fact]
    public async Task Render_StatesEveryUnusableSourceExplicitly()
    {
        var snapshot = await new InventoryScanner(new IsolatedRunner(new TestLog())).ScanAsync();

        var text = SnapshotReport.Render(snapshot);

        foreach (var fragment in snapshot.UnusableSources)
        {
            Assert.Contains(fragment.Source, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Render_AlwaysNamesTheMachineAndCaptureTime()
    {
        var snapshot = await new InventoryScanner(new IsolatedRunner(new TestLog())).ScanAsync();

        var text = SnapshotReport.Render(snapshot);

        Assert.Contains(snapshot.MachineName, text, StringComparison.Ordinal);
        Assert.Contains("Capture", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ToEnvelope_ProducesSerializableCapture()
    {
        var snapshot = await new InventoryScanner(new IsolatedRunner(new TestLog())).ScanAsync();

        var json = SnapshotSerializer.Serialize(SnapshotReport.ToEnvelope(snapshot, "0.2.0"));
        var restored = SnapshotSerializer.Deserialize(json);

        Assert.Equal(snapshot.MachineName, restored.MachineName);
        Assert.Equal(5, restored.Fragments.Count);
        Assert.Contains(restored.Fragments, f => f.Source == "gpu");
    }
}
```

- [ ] **Step 2 : Lancer les tests pour vérifier l'échec**

Run : `dotnet test ForgeConflict.sln --filter FullyQualifiedName~SnapshotReportTests`
Expected : échec de compilation, `SnapshotReport` étant introuvable.

- [ ] **Step 3 : Écrire le rapport**

Create `src/ForgeConflict.Core/Inventory/SnapshotReport.cs` :

```csharp
using System.Globalization;
using System.Text;
using System.Text.Json;
using ForgeConflict.Core.Collection;

namespace ForgeConflict.Core.Inventory;

/// <summary>
/// Met un instantane en forme lisible et en enveloppe serialisable. Les sources
/// inexploitables sont toujours nommees : c'est ce qui empeche un inventaire partiel
/// d'etre lu comme un inventaire complet.
/// </summary>
public static class SnapshotReport
{
    public static string Render(SystemSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var text = new StringBuilder();
        text.AppendLine(CultureInfo.CurrentCulture, $"Machine  : {snapshot.MachineName}");
        text.AppendLine(CultureInfo.CurrentCulture, $"Systeme  : {snapshot.OsVersion}");
        text.AppendLine(CultureInfo.CurrentCulture, $"Capture  : {snapshot.CapturedAt:g}");
        text.AppendLine();

        if (snapshot.Gpus.IsUsable && snapshot.Gpus.Value is { } gpus)
        {
            text.AppendLine("--- Adaptateurs graphiques ---");
            foreach (var gpu in gpus)
            {
                text.AppendLine(CultureInfo.CurrentCulture,
                    $"  {gpu.Name} [{gpu.Vendor}] pilote {gpu.DriverVersion ?? "?"} du {gpu.DriverDate:d} ({gpu.InfName ?? "inf inconnu"})");
            }
            text.AppendLine();
        }

        if (snapshot.GraphicsSettings.IsUsable && snapshot.GraphicsSettings.Value is { } settings)
        {
            text.AppendLine("--- Reglages graphiques ---");
            foreach (var setting in new[]
                     {
                         settings.TdrLevel, settings.TdrDelay, settings.TdrDdiDelay,
                         settings.HardwareAcceleratedScheduling, settings.MultiPlaneOverlay,
                     })
            {
                text.AppendLine(CultureInfo.CurrentCulture, $"  {setting.Describe()}");
            }
            text.AppendLine();
        }

        if (snapshot.DriverPackages.IsUsable && snapshot.DriverPackages.Value is { } packages)
        {
            text.AppendLine(CultureInfo.CurrentCulture,
                $"--- Magasin de pilotes : {packages.Count} paquet(s) d'affichage ---");
            foreach (var package in packages)
            {
                var signature = package.IsUnsigned ? "NON SIGNE" : "signe";
                text.AppendLine(CultureInfo.CurrentCulture,
                    $"  {package.PublishedName} <- {package.OriginalName ?? "?"} | {package.ProviderName ?? "?"} | v{package.DriverVersion ?? "?"} | {signature}");
            }
            text.AppendLine();
        }

        if (snapshot.Services.IsUsable && snapshot.Services.Value is { } services)
        {
            text.AppendLine(CultureInfo.CurrentCulture, $"--- Services graphiques : {services.Count} ---");
            foreach (var service in services)
            {
                text.AppendLine(CultureInfo.CurrentCulture,
                    $"  {service.Name} [{service.RelatedVendor}] {service.State} / {service.StartMode}");
            }
            text.AppendLine();
        }

        if (snapshot.OverlayProcesses.IsUsable && snapshot.OverlayProcesses.Value is { } overlays)
        {
            text.AppendLine(CultureInfo.CurrentCulture, $"--- Overlays actifs : {overlays.Count} ---");
            foreach (var process in overlays)
            {
                text.AppendLine(CultureInfo.CurrentCulture,
                    $"  {process.Name} (PID {process.Id}) {process.Company ?? ""}");
            }
            text.AppendLine();
        }

        if (snapshot.UnusableSources.Count > 0)
        {
            text.AppendLine("--- Sources indisponibles ---");
            foreach (var fragment in snapshot.UnusableSources)
            {
                text.AppendLine(CultureInfo.CurrentCulture,
                    $"  {fragment.Source} : {fragment.Status} {fragment.Error ?? string.Empty}");
            }
            text.AppendLine();
            text.AppendLine("Le diagnostic est incomplet : les sources ci-dessus n'ont pas repondu.");
        }
        else
        {
            text.AppendLine("Toutes les sources ont repondu.");
        }

        return text.ToString();
    }

    public static SnapshotEnvelope ToEnvelope(SystemSnapshot snapshot, string appVersion)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var records = new List<FragmentRecord>
        {
            Record(snapshot.Gpus),
            Record(snapshot.DriverPackages),
            Record(snapshot.Services),
            Record(snapshot.GraphicsSettings),
            Record(snapshot.OverlayProcesses),
        };

        return new SnapshotEnvelope(
            SnapshotEnvelope.CurrentSchemaVersion,
            snapshot.CapturedAt,
            snapshot.MachineName,
            snapshot.OsVersion,
            appVersion,
            records);
    }

    private static FragmentRecord Record<T>(Fragment<T> fragment) =>
        new(fragment.Source,
            fragment.Status,
            fragment.Error,
            fragment.Duration.TotalMilliseconds,
            fragment.IsUsable ? JsonSerializer.Serialize(fragment.Value) : null);
}
```

- [ ] **Step 4 : Passer la version à 0.2.0**

Modify `Directory.Build.props` — remplacer la ligne de version :

```xml
    <Version>0.2.0</Version>
```

- [ ] **Step 5 : Brancher l'inventaire sur l'interface**

Modify `src/ForgeConflict.App/MainWindow.xaml` — remplacer le `StackPanel` de boutons :

```xml
        <StackPanel Grid.Row="2" Orientation="Horizontal">
            <Button x:Name="Scan" Content="Analyser la machine" Padding="16,8" Click="OnScan" />
            <Button x:Name="Export" Content="Exporter l'instantane" Padding="16,8" Margin="12,0,0,0" Click="OnExport" />
            <Button x:Name="SelfTest" Content="Verifier le socle" Padding="16,8" Margin="12,0,0,0" Click="OnSelfTest" />
            <Button x:Name="Provoke" Content="Provoquer une exception" Padding="16,8" Margin="12,0,0,0" Click="OnProvoke" />
        </StackPanel>
```

- [ ] **Step 6 : Écrire les gestionnaires**

Modify `src/ForgeConflict.App/MainWindow.xaml.cs` — ajouter les usings et les deux méthodes.

Remplacer le bloc d'usings par :

```csharp
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Windows;
using ForgeConflict.Core.Collection;
using ForgeConflict.Core.Inventory;
using ForgeConflict.Core.Logging;
```

Ajouter le champ après `_log` :

```csharp
    private SystemSnapshot? _lastSnapshot;
```

Ajouter les deux gestionnaires avant `OnProvoke` :

```csharp
    private async void OnScan(object sender, RoutedEventArgs e)
    {
        Scan.IsEnabled = false;
        Output.Text = "Analyse en cours...";
        try
        {
            var scanner = new InventoryScanner(new IsolatedRunner(_log));
            _lastSnapshot = await scanner.ScanAsync();
            Output.Text = SnapshotReport.Render(_lastSnapshot);
            _log.Write(LogLevel.Info,
                $"analyse terminee, {_lastSnapshot.UnusableSources.Count} source(s) indisponible(s)");
        }
        finally
        {
            Scan.IsEnabled = true;
        }
    }

    private void OnExport(object sender, RoutedEventArgs e)
    {
        if (_lastSnapshot is null)
        {
            Output.Text = "Lancez d'abord une analyse.";
            return;
        }

        var directory = Path.Combine(AppPaths.BaseDirectory, "snapshots");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"snapshot-{DateTime.Now:yyyyMMdd-HHmmss}.json");

        File.WriteAllText(
            path,
            SnapshotSerializer.Serialize(SnapshotReport.ToEnvelope(_lastSnapshot, ThisAssembly.Version)),
            Encoding.UTF8);

        Output.Text = $"Instantane ecrit dans :\r\n{path}";
        _log.Write(LogLevel.Info, $"instantane exporte : {path}");
    }
```

- [ ] **Step 7 : Lancer les tests et compiler**

Run : `dotnet test ForgeConflict.sln --nologo`
Expected : `Failed: 0`, tous les tests des lots 1A et 1B passent.

- [ ] **Step 8 : Publier et vérifier sur la machine réelle**

Run : `powershell -ExecutionPolicy Bypass -File .\build.ps1`
Expected : `ForgeConflict 0.2.0`, `0 fichier(s) annexe(s)`.

Lancer l'exécutable et cliquer sur « Analyser la machine ». Vérifier :
1. Les deux GPU apparaissent avec leur fournisseur déduit de l'identifiant PCI ;
2. Les réglages TDR indiquent « valeur non definie, Windows applique … » — jamais zéro ;
3. Les paquets du magasin de pilotes sont listés avec leur signataire ;
4. Le bouton d'export produit un JSON relisible.

- [ ] **Step 9 : Vérifier dans la VM**

La VM n'a qu'un adaptateur `Microsoft Hyper-V Video` : c'est exactement le cas limite qui doit être
géré proprement plutôt que planter.

Déposer l'exécutable puis lancer la vérification :

```powershell
Copy-VMFile -Name 'W11 dev' -SourcePath 'D:\Projet\ForgeConflict\publish\ForgeConflict.exe' `
            -DestinationPath 'C:\ForgeConflict\ForgeConflict.exe' -FileSource Host -Force
```

Attendu dans l'invité : l'analyse aboutit, l'adaptateur Hyper-V est signalé comme pilote générique,
aucune source ne plante. Récupérer la preuve avec `tools/read-vm-report.ps1`.

- [ ] **Step 10 : Commit**

```bash
git add -A
git commit -m "Wire inventory scan into the shell and bump to 0.2.0"
git push origin lot1a-socle
```

---

## Ce que ce lot ne fait pas

Aucune règle de détection, aucune correction, aucune écriture. L'inventaire constate.

Les cinq collecteurs restants du spec section 4 — journal d'événements, dumps de plantage, matériel
et PCIe, alimentation, écrans — arrivent au **lot 1C**, avec les façades P/Invoke de
`ForgeConflict.Native` qu'ils nécessitent.
