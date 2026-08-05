# ForgeConflict — Lot 1A : socle technique

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Poser un exécutable WPF qui démarre en administrateur, journalise, exécute du travail de fond isolé et borné dans le temps, et ne se ferme jamais sur une exception non gérée.

**Architecture:** Trois assemblages. `Core` contient la logique sans aucune référence à WPF. `Native` reste vide à ce stade (créé pour figer la frontière P/Invoke). `App` est une coque WPF fine qui câble le journal, le garde-fou d'exceptions et affiche l'état. Les tests portent sur `Core` uniquement.

**Tech Stack:** .NET 8 (SDK 8.0.423), C# 12, WPF, xUnit.

**Référence:** `docs/superpowers/specs/2026-08-05-forgeconflict-design.md`, sections 3, 11 et 14.

**Contrainte de test:** ce lot n'écrit rien dans le système — pas de registre, pas de service, pas de `bcdedit`. Il peut donc être développé et testé sur la machine hôte. Dès le lot 1B, la règle VM de la section 14 du spec s'applique.

---

### Task 1 : Squelette de la solution

**Files:**
- Create: `ForgeConflict.sln`
- Create: `Directory.Build.props`
- Create: `src/ForgeConflict.Core/ForgeConflict.Core.csproj`
- Create: `src/ForgeConflict.Native/ForgeConflict.Native.csproj`
- Create: `src/ForgeConflict.App/ForgeConflict.App.csproj`
- Create: `tests/ForgeConflict.Tests/ForgeConflict.Tests.csproj`

- [ ] **Step 1 : Créer les propriétés communes**

Create `Directory.Build.props` :

```xml
<Project>
  <PropertyGroup>
    <Version>0.1.0</Version>
    <TargetFramework>net8.0-windows</TargetFramework>
    <LangVersion>12.0</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <Platforms>x64</Platforms>
    <PlatformTarget>x64</PlatformTarget>
  </PropertyGroup>
</Project>
```

- [ ] **Step 2 : Générer la solution et les projets**

Run :

```powershell
Set-Location 'D:\Projet\ForgeConflict'
dotnet new sln -n ForgeConflict
dotnet new classlib -o src/ForgeConflict.Core -n ForgeConflict.Core
dotnet new classlib -o src/ForgeConflict.Native -n ForgeConflict.Native
dotnet new wpf      -o src/ForgeConflict.App  -n ForgeConflict.App
dotnet new xunit    -o tests/ForgeConflict.Tests -n ForgeConflict.Tests
Remove-Item src/ForgeConflict.Core/Class1.cs, src/ForgeConflict.Native/Class1.cs -ErrorAction SilentlyContinue
dotnet sln add src/ForgeConflict.Core src/ForgeConflict.Native src/ForgeConflict.App tests/ForgeConflict.Tests
dotnet add src/ForgeConflict.App reference src/ForgeConflict.Core src/ForgeConflict.Native
dotnet add src/ForgeConflict.Core reference src/ForgeConflict.Native
dotnet add tests/ForgeConflict.Tests reference src/ForgeConflict.Core
```

- [ ] **Step 3 : Retirer les propriétés dupliquées des modèles**

Les modèles `dotnet new` écrivent leur propre `<TargetFramework>`, `<Nullable>` et
`<ImplicitUsings>` dans chaque `.csproj`. Ces valeurs locales masquent `Directory.Build.props` et
feront diverger les projets à la première montée de version.

Dans chacun des quatre `.csproj`, supprimer les lignes `<TargetFramework>`, `<Nullable>` et
`<ImplicitUsings>`. Conserver tout le reste, notamment `<UseWPF>true</UseWPF>` dans
`ForgeConflict.App.csproj` et `<IsPackable>false</IsPackable>` dans le projet de test.

Vérifier qu'il n'en reste aucune :

```powershell
Select-String -Path (Get-ChildItem -Recurse -Filter *.csproj).FullName -Pattern 'TargetFramework|ImplicitUsings|<Nullable>'
```

Expected : aucune correspondance.

- [ ] **Step 4 : Compiler pour vérifier le squelette**

Run : `dotnet build ForgeConflict.sln -c Debug`
Expected : `Build succeeded.` avec `0 Error(s)`.

Si des avertissements bloquent à cause de `TreatWarningsAsErrors`, corriger le code généré par les modèles plutôt que de désactiver l'option.

- [ ] **Step 5 : Commit**

```bash
git add -A
git commit -m "Add solution skeleton with Core, Native, App and test projects"
```

---

### Task 2 : Journalisation fichier

Format imposé par les conventions du projet : `[ISO8601] [NIVEAU] message`, rotation au-delà de 1 Mo vers `archive/`.

**Files:**
- Create: `src/ForgeConflict.Core/Logging/LogLevel.cs`
- Create: `src/ForgeConflict.Core/Logging/ILog.cs`
- Create: `src/ForgeConflict.Core/Logging/FileLog.cs`
- Create: `tests/ForgeConflict.Tests/Logging/FileLogTests.cs`

- [ ] **Step 1 : Écrire les tests qui échouent**

Create `tests/ForgeConflict.Tests/Logging/FileLogTests.cs` :

```csharp
using ForgeConflict.Core.Logging;

namespace ForgeConflict.Tests.Logging;

public sealed class FileLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "fc-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Write_FormatsWithIso8601AndLevel()
    {
        var fixedClock = new DateTimeOffset(2026, 8, 5, 14, 30, 0, TimeSpan.Zero);
        using (var log = new FileLog(_dir, maxBytes: 1_048_576, clock: () => fixedClock))
        {
            log.Write(LogLevel.Info, "demarrage");
        }

        var line = File.ReadAllLines(Path.Combine(_dir, "forgeconflict.log")).Single();
        Assert.Equal("[2026-08-05T14:30:00.0000000+00:00] [INFO] demarrage", line);
    }

    [Fact]
    public void Write_AppendsExceptionDetailOnNextLines()
    {
        using (var log = new FileLog(_dir, maxBytes: 1_048_576, clock: () => DateTimeOffset.UtcNow))
        {
            log.Write(LogLevel.Error, "echec", new InvalidOperationException("boum"));
        }

        var text = File.ReadAllText(Path.Combine(_dir, "forgeconflict.log"));
        Assert.Contains("[ERROR] echec", text, StringComparison.Ordinal);
        Assert.Contains("InvalidOperationException", text, StringComparison.Ordinal);
        Assert.Contains("boum", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_RotatesToArchiveOnceMaxBytesExceeded()
    {
        using (var log = new FileLog(_dir, maxBytes: 256, clock: () => DateTimeOffset.UtcNow))
        {
            for (var i = 0; i < 40; i++) log.Write(LogLevel.Info, $"ligne numero {i}");
        }

        var archived = Directory.GetFiles(Path.Combine(_dir, "archive"));
        Assert.NotEmpty(archived);
        Assert.True(new FileInfo(Path.Combine(_dir, "forgeconflict.log")).Length <= 256 * 2);
    }

    [Fact]
    public void Write_NeverThrowsWhenDirectoryCannotBeCreated()
    {
        // Un fichier occupe l'emplacement exact du repertoire vise : la creation
        // echouera a coup sur. Le journal ne peut jamais devenir la cause d'un
        // plantage de l'application.
        Directory.CreateDirectory(_dir);
        var blocked = Path.Combine(_dir, "bloque");
        File.WriteAllText(blocked, "je suis un fichier, pas un repertoire");

        using var log = new FileLog(blocked, maxBytes: 1024, clock: () => DateTimeOffset.UtcNow);
        var ex = Record.Exception(() => log.Write(LogLevel.Warn, "message"));

        Assert.Null(ex);
    }
}
```

- [ ] **Step 2 : Lancer les tests pour vérifier l'échec**

Run : `dotnet test tests/ForgeConflict.Tests --filter FullyQualifiedName~FileLogTests`
Expected : échec de compilation, `LogLevel`, `ILog` et `FileLog` étant introuvables.

- [ ] **Step 3 : Écrire le niveau et le contrat**

Create `src/ForgeConflict.Core/Logging/LogLevel.cs` :

```csharp
namespace ForgeConflict.Core.Logging;

public enum LogLevel
{
    Debug,
    Info,
    Warn,
    Error,
}
```

Create `src/ForgeConflict.Core/Logging/ILog.cs` :

```csharp
namespace ForgeConflict.Core.Logging;

/// <summary>
/// Journal de l'application. Une implementation ne doit jamais lever d'exception :
/// un echec d'ecriture du journal ne peut pas devenir une cause de plantage.
/// </summary>
public interface ILog
{
    void Write(LogLevel level, string message, Exception? exception = null);
}
```

- [ ] **Step 4 : Écrire l'implémentation**

Create `src/ForgeConflict.Core/Logging/FileLog.cs` :

```csharp
using System.Text;

namespace ForgeConflict.Core.Logging;

public sealed class FileLog : ILog, IDisposable
{
    private const string FileName = "forgeconflict.log";

    private readonly string _directory;
    private readonly string _path;
    private readonly string _archiveDirectory;
    private readonly long _maxBytes;
    private readonly Func<DateTimeOffset> _clock;
    private readonly object _gate = new();
    private volatile bool _disposed;

    public FileLog(string directory, long maxBytes, Func<DateTimeOffset> clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);
        ArgumentNullException.ThrowIfNull(clock);

        _directory = directory;
        _archiveDirectory = Path.Combine(directory, "archive");
        _path = Path.Combine(directory, FileName);
        _maxBytes = maxBytes;
        _clock = clock;
    }

    public void Write(LogLevel level, string message, Exception? exception = null)
    {
        if (_disposed) return;

        try
        {
            var builder = new StringBuilder();
            builder.Append('[').Append(_clock().ToString("o")).Append("] [")
                   .Append(level.ToString().ToUpperInvariant()).Append("] ")
                   .AppendLine(message);

            if (exception is not null)
            {
                builder.AppendLine(exception.ToString());
            }

            lock (_gate)
            {
                Directory.CreateDirectory(_directory);
                RotateIfNeeded();
                File.AppendAllText(_path, builder.ToString(), Encoding.UTF8);
            }
        }
        catch (Exception)
        {
            // Volontairement avale : voir le contrat de ILog. Ecrire ailleurs serait
            // exposer le meme risque d'echec.
        }
    }

    private void RotateIfNeeded()
    {
        var info = new FileInfo(_path);
        if (!info.Exists || info.Length < _maxBytes) return;

        Directory.CreateDirectory(_archiveDirectory);
        var stamp = _clock().ToString("yyyyMMdd-HHmmss-fff");
        File.Move(_path, Path.Combine(_archiveDirectory, $"forgeconflict-{stamp}.log"), overwrite: true);
    }

    public void Dispose() => _disposed = true;
}
```

- [ ] **Step 5 : Lancer les tests pour vérifier le succès**

Run : `dotnet test tests/ForgeConflict.Tests --filter FullyQualifiedName~FileLogTests`
Expected : `Passed! - Failed: 0, Passed: 4`.

- [ ] **Step 6 : Commit**

```bash
git add src/ForgeConflict.Core/Logging tests/ForgeConflict.Tests/Logging
git commit -m "Add file log with ISO8601 format and size-based rotation"
```

---

### Task 3 : Fragment tolérant aux pannes

Le spec impose qu'un collecteur défaillant n'interrompe jamais le scan et que l'interface distingue une donnée absente d'une donnée saine. `Fragment<T>` porte cette distinction.

**Files:**
- Create: `src/ForgeConflict.Core/Collection/FragmentStatus.cs`
- Create: `src/ForgeConflict.Core/Collection/Fragment.cs`
- Create: `tests/ForgeConflict.Tests/Collection/FragmentTests.cs`

- [ ] **Step 1 : Écrire les tests qui échouent**

Create `tests/ForgeConflict.Tests/Collection/FragmentTests.cs` :

```csharp
using ForgeConflict.Core.Collection;

namespace ForgeConflict.Tests.Collection;

public sealed class FragmentTests
{
    [Fact]
    public void Ok_CarriesValueAndSuccessStatus()
    {
        var fragment = Fragment<int>.Ok("gpu", 42, TimeSpan.FromMilliseconds(12));

        Assert.Equal(FragmentStatus.Ok, fragment.Status);
        Assert.Equal(42, fragment.Value);
        Assert.Null(fragment.Error);
        Assert.True(fragment.IsUsable);
    }

    [Fact]
    public void Failed_CarriesErrorAndNoValue()
    {
        var fragment = Fragment<int>.Failed("gpu", "acces refuse", TimeSpan.Zero);

        Assert.Equal(FragmentStatus.Failed, fragment.Status);
        Assert.Equal(0, fragment.Value);
        Assert.Equal("acces refuse", fragment.Error);
        Assert.False(fragment.IsUsable);
    }

    [Fact]
    public void TimedOut_IsDistinctFromFailed()
    {
        var fragment = Fragment<int>.TimedOut("wmi", TimeSpan.FromSeconds(5));

        Assert.Equal(FragmentStatus.TimedOut, fragment.Status);
        Assert.False(fragment.IsUsable);
        Assert.NotEqual(FragmentStatus.Failed, fragment.Status);
    }
}
```

- [ ] **Step 2 : Lancer les tests pour vérifier l'échec**

Run : `dotnet test tests/ForgeConflict.Tests --filter FullyQualifiedName~FragmentTests`
Expected : échec de compilation, `Fragment` et `FragmentStatus` étant introuvables.

- [ ] **Step 3 : Écrire l'implémentation**

Create `src/ForgeConflict.Core/Collection/FragmentStatus.cs` :

```csharp
namespace ForgeConflict.Core.Collection;

public enum FragmentStatus
{
    /// <summary>La collecte a abouti, la valeur est exploitable.</summary>
    Ok,

    /// <summary>La collecte a leve une exception. La cause est dans Error.</summary>
    Failed,

    /// <summary>La collecte n'a pas rendu la main dans le delai imparti.</summary>
    TimedOut,
}
```

Create `src/ForgeConflict.Core/Collection/Fragment.cs` :

```csharp
namespace ForgeConflict.Core.Collection;

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
    TimeSpan Duration)
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

- [ ] **Step 4 : Lancer les tests pour vérifier le succès**

Run : `dotnet test tests/ForgeConflict.Tests --filter FullyQualifiedName~FragmentTests`
Expected : `Passed! - Failed: 0, Passed: 3`.

- [ ] **Step 5 : Commit**

```bash
git add src/ForgeConflict.Core/Collection tests/ForgeConflict.Tests/Collection
git commit -m "Add Fragment result type distinguishing failure from timeout"
```

---

### Task 4 : Exécuteur isolé avec délai

Point technique important : un appel WMI bloqué n'observe pas le jeton d'annulation. Attendre la tâche ne suffit donc pas — il faut cesser de l'attendre et la laisser mourir en fond.

**Files:**
- Create: `src/ForgeConflict.Core/Collection/IIsolatedRunner.cs`
- Create: `src/ForgeConflict.Core/Collection/IsolatedRunner.cs`
- Create: `tests/ForgeConflict.Tests/Collection/IsolatedRunnerTests.cs`
- Create: `tests/ForgeConflict.Tests/TestLog.cs`

- [ ] **Step 1 : Écrire le journal de test**

Create `tests/ForgeConflict.Tests/TestLog.cs` :

```csharp
using System.Collections.Concurrent;
using ForgeConflict.Core.Logging;

namespace ForgeConflict.Tests;

public sealed class TestLog : ILog
{
    public ConcurrentQueue<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = new();

    public void Write(LogLevel level, string message, Exception? exception = null) =>
        Entries.Enqueue((level, message, exception));

    public bool Contains(LogLevel level, string fragment) =>
        Entries.Any(e => e.Level == level && e.Message.Contains(fragment, StringComparison.OrdinalIgnoreCase));
}
```

- [ ] **Step 2 : Écrire les tests qui échouent**

Create `tests/ForgeConflict.Tests/Collection/IsolatedRunnerTests.cs` :

```csharp
using System.Diagnostics;
using ForgeConflict.Core.Collection;
using ForgeConflict.Core.Logging;

namespace ForgeConflict.Tests.Collection;

public sealed class IsolatedRunnerTests
{
    [Fact]
    public async Task RunAsync_ReturnsOkWhenWorkSucceeds()
    {
        var runner = new IsolatedRunner(new TestLog());

        var fragment = await runner.RunAsync("gpu", _ => 7, TimeSpan.FromSeconds(5));

        Assert.Equal(FragmentStatus.Ok, fragment.Status);
        Assert.Equal(7, fragment.Value);
    }

    [Fact]
    public async Task RunAsync_CapturesExceptionInsteadOfPropagating()
    {
        var log = new TestLog();
        var runner = new IsolatedRunner(log);

        var fragment = await runner.RunAsync<int>(
            "registre",
            _ => throw new UnauthorizedAccessException("acces refuse"),
            TimeSpan.FromSeconds(5));

        Assert.Equal(FragmentStatus.Failed, fragment.Status);
        Assert.Contains("acces refuse", fragment.Error!, StringComparison.Ordinal);
        Assert.True(log.Contains(LogLevel.Error, "registre"));
    }

    [Fact]
    public async Task RunAsync_ReturnsTimedOutWithoutWaitingForBlockedWork()
    {
        var log = new TestLog();
        var runner = new IsolatedRunner(log);
        using var release = new ManualResetEventSlim(false);
        var stopwatch = Stopwatch.StartNew();

        var fragment = await runner.RunAsync<int>(
            "wmi",
            _ => { release.Wait(TimeSpan.FromSeconds(30)); return 1; },
            TimeSpan.FromMilliseconds(200));

        stopwatch.Stop();
        release.Set();

        Assert.Equal(FragmentStatus.TimedOut, fragment.Status);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5),
            $"l'appel a rendu la main en {stopwatch.Elapsed}, il ne doit pas attendre le travail bloque");
        Assert.True(log.Contains(LogLevel.Warn, "wmi"));
    }

    [Fact]
    public async Task RunAsync_IsolatesFailuresBetweenConcurrentCollectors()
    {
        var runner = new IsolatedRunner(new TestLog());

        var results = await Task.WhenAll(
            runner.RunAsync<int>("a", _ => throw new InvalidOperationException("boum"), TimeSpan.FromSeconds(5)),
            runner.RunAsync("b", _ => 2, TimeSpan.FromSeconds(5)),
            runner.RunAsync("c", _ => 3, TimeSpan.FromSeconds(5)));

        Assert.Equal(FragmentStatus.Failed, results[0].Status);
        Assert.Equal(FragmentStatus.Ok, results[1].Status);
        Assert.Equal(FragmentStatus.Ok, results[2].Status);
    }
}
```

- [ ] **Step 3 : Lancer les tests pour vérifier l'échec**

Run : `dotnet test tests/ForgeConflict.Tests --filter FullyQualifiedName~IsolatedRunnerTests`
Expected : échec de compilation, `IsolatedRunner` étant introuvable.

- [ ] **Step 4 : Écrire le contrat**

Create `src/ForgeConflict.Core/Collection/IIsolatedRunner.cs` :

```csharp
namespace ForgeConflict.Core.Collection;

/// <summary>
/// Execute un travail de collecte en isolation : ni exception ni blocage ne remontent
/// a l'appelant. C'est la garantie qu'un collecteur defaillant n'interrompt pas le scan.
/// </summary>
public interface IIsolatedRunner
{
    Task<Fragment<T>> RunAsync<T>(
        string source,
        Func<CancellationToken, T> work,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 5 : Écrire l'implémentation**

Create `src/ForgeConflict.Core/Collection/IsolatedRunner.cs` :

```csharp
using System.Diagnostics;
using ForgeConflict.Core.Logging;

namespace ForgeConflict.Core.Collection;

public sealed class IsolatedRunner : IIsolatedRunner
{
    private readonly ILog _log;

    public IsolatedRunner(ILog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        _log = log;
    }

    public async Task<Fragment<T>> RunAsync<T>(
        string source,
        Func<CancellationToken, T> work,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(work);

        var stopwatch = Stopwatch.StartNew();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // CancellationToken.None : la tache doit pouvoir demarrer meme si l'annulation
        // est deja demandee, sinon on ne distingue plus l'echec du delai depasse.
        var task = Task.Run(() => work(cts.Token), CancellationToken.None);

        var finished = await Task.WhenAny(task, Task.Delay(timeout, CancellationToken.None)).ConfigureAwait(false);

        if (!ReferenceEquals(finished, task))
        {
            cts.Cancel();

            // Un appel natif bloque n'observe pas le jeton. On cesse de l'attendre,
            // mais on observe son exception eventuelle pour ne pas declencher
            // UnobservedTaskException, et on libere les ressources ensuite.
            _ = task.ContinueWith(
                t => { _ = t.Exception; cts.Dispose(); },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            _log.Write(LogLevel.Warn, $"collecteur {source} : delai depasse apres {timeout.TotalSeconds:0.#} s");
            return Fragment<T>.TimedOut(source, stopwatch.Elapsed);
        }

        try
        {
            var value = await task.ConfigureAwait(false);
            return Fragment<T>.Ok(source, value, stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            _log.Write(LogLevel.Error, $"collecteur {source} : echec de la collecte", ex);
            return Fragment<T>.Failed(source, ex.Message, stopwatch.Elapsed);
        }
        finally
        {
            cts.Dispose();
        }
    }
}
```

- [ ] **Step 6 : Lancer les tests pour vérifier le succès**

Run : `dotnet test tests/ForgeConflict.Tests --filter FullyQualifiedName~IsolatedRunnerTests`
Expected : `Passed! - Failed: 0, Passed: 4`.

- [ ] **Step 7 : Commit**

```bash
git add src/ForgeConflict.Core/Collection tests/ForgeConflict.Tests
git commit -m "Add isolated runner that survives collector exceptions and hangs"
```

---

### Task 5 : Garde-fou d'exceptions

La logique est séparée de WPF pour être testable. L'adaptateur WPF viendra en Task 7.

**Files:**
- Create: `src/ForgeConflict.Core/Diagnostics/ExceptionGuard.cs`
- Create: `tests/ForgeConflict.Tests/Diagnostics/ExceptionGuardTests.cs`

- [ ] **Step 1 : Écrire les tests qui échouent**

Create `tests/ForgeConflict.Tests/Diagnostics/ExceptionGuardTests.cs` :

```csharp
using ForgeConflict.Core.Diagnostics;
using ForgeConflict.Core.Logging;

namespace ForgeConflict.Tests.Diagnostics;

public sealed class ExceptionGuardTests
{
    [Fact]
    public void HandleUi_LogsAndReportsHandled()
    {
        var log = new TestLog();
        var guard = new ExceptionGuard(log);

        var handled = guard.HandleUi(new InvalidOperationException("boum"));

        Assert.True(handled);
        Assert.True(log.Contains(LogLevel.Error, "interface"));
    }

    [Fact]
    public void HandleDomain_LogsEvenWhenObjectIsNotAnException()
    {
        var log = new TestLog();
        var guard = new ExceptionGuard(log);

        guard.HandleDomain(exceptionObject: "objet non typé", isTerminating: true);

        Assert.True(log.Contains(LogLevel.Error, "domaine"));
    }

    [Fact]
    public void HandleUnobservedTask_LogsAndReportsObserved()
    {
        var log = new TestLog();
        var guard = new ExceptionGuard(log);

        var observed = guard.HandleUnobservedTask(new AggregateException(new TimeoutException()));

        Assert.True(observed);
        Assert.True(log.Contains(LogLevel.Error, "tache"));
    }

    [Fact]
    public void HandleUi_RecordsEveryExceptionForTheSessionReport()
    {
        var guard = new ExceptionGuard(new TestLog());

        guard.HandleUi(new InvalidOperationException("un"));
        guard.HandleUi(new InvalidOperationException("deux"));

        Assert.Equal(2, guard.HandledCount);
    }
}
```

- [ ] **Step 2 : Lancer les tests pour vérifier l'échec**

Run : `dotnet test tests/ForgeConflict.Tests --filter FullyQualifiedName~ExceptionGuardTests`
Expected : échec de compilation, `ExceptionGuard` étant introuvable.

- [ ] **Step 3 : Écrire l'implémentation**

Create `src/ForgeConflict.Core/Diagnostics/ExceptionGuard.cs` :

```csharp
using ForgeConflict.Core.Logging;

namespace ForgeConflict.Core.Diagnostics;

/// <summary>
/// Recueille les exceptions non gerees. L'application manipule des pilotes graphiques :
/// se fermer brutalement en pleine operation serait la pire issue possible, donc rien
/// ne doit provoquer un arret non maitrise.
/// </summary>
public sealed class ExceptionGuard
{
    private readonly ILog _log;
    private int _handledCount;

    public ExceptionGuard(ILog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        _log = log;
    }

    public int HandledCount => Volatile.Read(ref _handledCount);

    /// <returns>Toujours vrai : l'exception est consideree traitee, l'interface survit.</returns>
    public bool HandleUi(Exception exception)
    {
        Interlocked.Increment(ref _handledCount);
        _log.Write(LogLevel.Error, "exception non geree sur le thread de l'interface", exception);
        return true;
    }

    public void HandleDomain(object? exceptionObject, bool isTerminating)
    {
        Interlocked.Increment(ref _handledCount);
        var exception = exceptionObject as Exception;
        var suffix = isTerminating ? " (arret en cours)" : string.Empty;
        _log.Write(LogLevel.Error, $"exception non geree du domaine{suffix} : {exceptionObject}", exception);
    }

    /// <returns>Toujours vrai : l'exception est marquee observee pour ne pas tuer le processus.</returns>
    public bool HandleUnobservedTask(Exception exception)
    {
        Interlocked.Increment(ref _handledCount);
        _log.Write(LogLevel.Error, "exception de tache non observee", exception);
        return true;
    }
}
```

- [ ] **Step 4 : Lancer les tests pour vérifier le succès**

Run : `dotnet test tests/ForgeConflict.Tests --filter FullyQualifiedName~ExceptionGuardTests`
Expected : `Passed! - Failed: 0, Passed: 4`.

- [ ] **Step 5 : Commit**

```bash
git add src/ForgeConflict.Core/Diagnostics tests/ForgeConflict.Tests/Diagnostics
git commit -m "Add exception guard that keeps the app alive on unhandled failures"
```

---

### Task 6 : Enveloppe d'instantané

Le spec prévoit de capturer l'état système en JSON pour rejouer les règles en tests unitaires, ce qui couvre les cas qu'une VM ne peut pas reproduire. L'enveloppe est définie ici ; les charges utiles typées arriveront au lot 1B.

**Files:**
- Create: `src/ForgeConflict.Core/Collection/FragmentRecord.cs`
- Create: `src/ForgeConflict.Core/Collection/SnapshotEnvelope.cs`
- Create: `src/ForgeConflict.Core/Collection/SnapshotSerializer.cs`
- Create: `tests/ForgeConflict.Tests/Collection/SnapshotSerializerTests.cs`

- [ ] **Step 1 : Écrire les tests qui échouent**

Create `tests/ForgeConflict.Tests/Collection/SnapshotSerializerTests.cs` :

```csharp
using ForgeConflict.Core.Collection;

namespace ForgeConflict.Tests.Collection;

public sealed class SnapshotSerializerTests
{
    private static SnapshotEnvelope Sample() => new(
        SchemaVersion: 1,
        CapturedAt: new DateTimeOffset(2026, 8, 5, 12, 0, 0, TimeSpan.Zero),
        MachineName: "BANC-TEST",
        OsVersion: "10.0.26100",
        AppVersion: "0.1.0",
        Fragments:
        [
            new FragmentRecord("gpu", FragmentStatus.Ok, null, 12.5, """{"count":2}"""),
            new FragmentRecord("wmi", FragmentStatus.TimedOut, null, 5000, null),
            new FragmentRecord("registre", FragmentStatus.Failed, "acces refuse", 3, null),
        ]);

    [Fact]
    public void RoundTrip_PreservesEveryField()
    {
        var json = SnapshotSerializer.Serialize(Sample());
        var restored = SnapshotSerializer.Deserialize(json);

        Assert.Equal("BANC-TEST", restored.MachineName);
        Assert.Equal(3, restored.Fragments.Count);
        Assert.Equal(FragmentStatus.TimedOut, restored.Fragments[1].Status);
        Assert.Equal("acces refuse", restored.Fragments[2].Error);
        Assert.Equal("""{"count":2}""", restored.Fragments[0].PayloadJson);
    }

    [Fact]
    public void Serialize_WritesStatusAsReadableText()
    {
        var json = SnapshotSerializer.Serialize(Sample());

        Assert.Contains("\"TimedOut\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"status\": 2", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Deserialize_RejectsMalformedJsonWithClearError()
    {
        var ex = Record.Exception(() => SnapshotSerializer.Deserialize("{ ceci n'est pas du json"));

        Assert.NotNull(ex);
        Assert.IsType<InvalidDataException>(ex);
    }

    [Fact]
    public void Deserialize_RejectsUnsupportedSchemaVersion()
    {
        var json = SnapshotSerializer.Serialize(Sample() with { SchemaVersion = 99 });

        var ex = Assert.Throws<InvalidDataException>(() => SnapshotSerializer.Deserialize(json));
        Assert.Contains("99", ex.Message, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 2 : Lancer les tests pour vérifier l'échec**

Run : `dotnet test tests/ForgeConflict.Tests --filter FullyQualifiedName~SnapshotSerializerTests`
Expected : échec de compilation, `SnapshotEnvelope` et `SnapshotSerializer` étant introuvables.

- [ ] **Step 3 : Écrire les modèles**

Create `src/ForgeConflict.Core/Collection/FragmentRecord.cs` :

```csharp
namespace ForgeConflict.Core.Collection;

/// <summary>
/// Forme serialisable d'un <see cref="Fragment{T}"/>. La charge utile reste du JSON brut :
/// l'enveloppe n'a pas a connaitre les types de chaque collecteur.
/// </summary>
public sealed record FragmentRecord(
    string Source,
    FragmentStatus Status,
    string? Error,
    double DurationMs,
    string? PayloadJson);
```

Create `src/ForgeConflict.Core/Collection/SnapshotEnvelope.cs` :

```csharp
namespace ForgeConflict.Core.Collection;

public sealed record SnapshotEnvelope(
    int SchemaVersion,
    DateTimeOffset CapturedAt,
    string MachineName,
    string OsVersion,
    string AppVersion,
    IReadOnlyList<FragmentRecord> Fragments)
{
    public const int CurrentSchemaVersion = 1;
}
```

- [ ] **Step 4 : Écrire le sérialiseur**

Create `src/ForgeConflict.Core/Collection/SnapshotSerializer.cs` :

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ForgeConflict.Core.Collection;

public static class SnapshotSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize(SnapshotEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        return JsonSerializer.Serialize(envelope, Options);
    }

    /// <exception cref="InvalidDataException">
    /// Le JSON est illisible ou porte une version de schema non supportee.
    /// </exception>
    public static SnapshotEnvelope Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        SnapshotEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<SnapshotEnvelope>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("L'instantane est illisible.", ex);
        }

        if (envelope is null)
        {
            throw new InvalidDataException("L'instantane est vide.");
        }

        if (envelope.SchemaVersion != SnapshotEnvelope.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Version de schema {envelope.SchemaVersion} non supportee, attendu {SnapshotEnvelope.CurrentSchemaVersion}.");
        }

        return envelope;
    }
}
```

- [ ] **Step 5 : Lancer les tests pour vérifier le succès**

Run : `dotnet test tests/ForgeConflict.Tests --filter FullyQualifiedName~SnapshotSerializerTests`
Expected : `Passed! - Failed: 0, Passed: 4`.

- [ ] **Step 6 : Commit**

```bash
git add src/ForgeConflict.Core/Collection tests/ForgeConflict.Tests/Collection
git commit -m "Add snapshot envelope with versioned JSON serialization"
```

---

### Task 7 : Coque applicative et élévation

**Files:**
- Create: `src/ForgeConflict.App/app.manifest`
- Create: `src/ForgeConflict.App/AppPaths.cs`
- Create: `src/ForgeConflict.App/ThisAssembly.cs`
- Modify: `src/ForgeConflict.App/ForgeConflict.App.csproj`
- Modify: `src/ForgeConflict.App/App.xaml.cs`
- Modify: `src/ForgeConflict.App/MainWindow.xaml`
- Modify: `src/ForgeConflict.App/MainWindow.xaml.cs`

- [ ] **Step 1 : Déclarer l'élévation obligatoire**

Create `src/ForgeConflict.App/app.manifest` :

```xml
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <assemblyIdentity version="0.1.0.0" name="ForgeConflict.App" />
  <trustInfo xmlns="urn:schemas-microsoft-com:asm.v2">
    <security>
      <requestedPrivileges xmlns="urn:schemas-microsoft-com:asm.v3">
        <!-- Le DriverStore et les cles pilote sont inaccessibles sans elevation. -->
        <requestedExecutionLevel level="requireAdministrator" uiAccess="false" />
      </requestedPrivileges>
    </security>
  </trustInfo>
  <compatibility xmlns="urn:schemas-microsoft-com:compatibility.v1">
    <application>
      <!-- Windows 10 et 11 -->
      <supportedOS Id="{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}" />
    </application>
  </compatibility>
  <application xmlns="urn:schemas-microsoft-com:asm.v3">
    <windowsSettings>
      <dpiAware xmlns="http://schemas.microsoft.com/SMI/2005/WindowsSettings">true/pm</dpiAware>
      <dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">permonitorv2</dpiAwareness>
    </windowsSettings>
  </application>
</assembly>
```

- [ ] **Step 2 : Référencer le manifeste**

Modify `src/ForgeConflict.App/ForgeConflict.App.csproj` — ajouter dans le `<PropertyGroup>` existant :

```xml
    <ApplicationManifest>app.manifest</ApplicationManifest>
    <AssemblyName>ForgeConflict</AssemblyName>
```

- [ ] **Step 3 : Centraliser les chemins**

Create `src/ForgeConflict.App/AppPaths.cs` :

```csharp
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
```

- [ ] **Step 4 : Câbler le journal et le garde-fou au démarrage**

Replace the whole content of `src/ForgeConflict.App/App.xaml.cs` :

```csharp
using System.Windows;
using System.Windows.Threading;
using ForgeConflict.Core.Diagnostics;
using ForgeConflict.Core.Logging;

namespace ForgeConflict.App;

public partial class App : Application
{
    private FileLog? _log;
    private ExceptionGuard? _guard;

    internal ILog Log => _log ?? throw new InvalidOperationException("Journal non initialise.");

    protected override void OnStartup(StartupEventArgs e)
    {
        _log = new FileLog(AppPaths.LogDirectory, AppPaths.LogRotationBytes, () => DateTimeOffset.Now);
        _guard = new ExceptionGuard(_log);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        _log.Write(LogLevel.Info, $"demarrage de ForgeConflict {ThisAssembly.Version}");
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _log?.Write(LogLevel.Info, $"arret, {_guard?.HandledCount ?? 0} exception(s) interceptee(s)");
        _log?.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e) =>
        e.Handled = _guard!.HandleUi(e.Exception);

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e) =>
        _guard!.HandleDomain(e.ExceptionObject, e.IsTerminating);

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        if (_guard!.HandleUnobservedTask(e.Exception)) e.SetObserved();
    }
}
```

- [ ] **Step 5 : Exposer la version**

Create `src/ForgeConflict.App/ThisAssembly.cs` :

```csharp
using System.Reflection;

namespace ForgeConflict.App;

internal static class ThisAssembly
{
    public static string Version { get; } =
        typeof(ThisAssembly).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion.Split('+')[0]
        ?? "0.0.0";
}
```

- [ ] **Step 6 : Écrire la fenêtre de vérification**

Replace the whole content of `src/ForgeConflict.App/MainWindow.xaml` :

```xml
<Window x:Class="ForgeConflict.App.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="ForgeConflict" Height="520" Width="820"
        WindowStartupLocation="CenterScreen">
    <Grid Margin="24">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
            <RowDefinition Height="Auto" />
        </Grid.RowDefinitions>

        <TextBlock Grid.Row="0" Text="ForgeConflict" FontSize="26" FontWeight="SemiBold" />

        <TextBox Grid.Row="1" x:Name="Output" Margin="0,16"
                 IsReadOnly="True" FontFamily="Consolas" FontSize="13"
                 VerticalScrollBarVisibility="Auto" TextWrapping="NoWrap" />

        <StackPanel Grid.Row="2" Orientation="Horizontal">
            <Button x:Name="SelfTest" Content="Verifier le socle" Padding="16,8" Click="OnSelfTest" />
            <Button x:Name="Provoke" Content="Provoquer une exception" Padding="16,8" Margin="12,0,0,0" Click="OnProvoke" />
        </StackPanel>
    </Grid>
</Window>
```

- [ ] **Step 7 : Écrire le code de la fenêtre**

Replace the whole content of `src/ForgeConflict.App/MainWindow.xaml.cs` :

```csharp
using System.Security.Principal;
using System.Text;
using System.Windows;
using ForgeConflict.Core.Collection;
using ForgeConflict.Core.Logging;

namespace ForgeConflict.App;

public partial class MainWindow : Window
{
    private readonly ILog _log = ((App)Application.Current).Log;

    public MainWindow() => InitializeComponent();

    private async void OnSelfTest(object sender, RoutedEventArgs e)
    {
        var runner = new IsolatedRunner(_log);
        var report = new StringBuilder();

        report.AppendLine($"Version        : {ThisAssembly.Version}");
        report.AppendLine($"Administrateur : {IsElevated()}");
        report.AppendLine($"Journal        : {AppPaths.LogDirectory}");
        report.AppendLine();

        var ok = await runner.RunAsync("succes", _ => "valeur collectee", TimeSpan.FromSeconds(2));
        var failed = await runner.RunAsync<string>(
            "echec", _ => throw new UnauthorizedAccessException("acces refuse"), TimeSpan.FromSeconds(2));
        var timedOut = await runner.RunAsync<string>(
            "blocage", ct => { Thread.Sleep(TimeSpan.FromSeconds(10)); return "jamais"; }, TimeSpan.FromMilliseconds(300));

        foreach (var fragment in new[] { ok, failed, timedOut })
        {
            report.AppendLine(
                $"{fragment.Source,-10} {fragment.Status,-10} {fragment.Duration.TotalMilliseconds,7:0} ms  {fragment.Error ?? fragment.Value ?? "-"}");
        }

        report.AppendLine();
        report.AppendLine("Le blocage doit rendre la main en ~300 ms, pas en 10 s.");
        Output.Text = report.ToString();
        _log.Write(LogLevel.Info, "verification du socle terminee");
    }

    private void OnProvoke(object sender, RoutedEventArgs e) =>
        throw new InvalidOperationException("exception volontaire de verification");

    private static string IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator) ? "oui" : "non";
    }
}
```

- [ ] **Step 8 : Compiler et lancer**

Run :

```powershell
dotnet build ForgeConflict.sln -c Debug
Start-Process 'D:\Projet\ForgeConflict\src\ForgeConflict.App\bin\x64\Debug\net8.0-windows\ForgeConflict.exe'
```

Expected : une invite UAC apparaît, puis la fenêtre s'ouvre.

Vérifier manuellement, dans cet ordre :
1. « Verifier le socle » affiche `Administrateur : oui`, une ligne `Ok`, une ligne `Failed`, une ligne `TimedOut` à environ 300 ms.
2. « Provoquer une exception » **ne ferme pas l'application** — la fenêtre reste utilisable.
3. `.logs\forgeconflict.log` à côté de l'exe contient la ligne de démarrage, l'avertissement de délai dépassé et l'exception provoquée.

- [ ] **Step 9 : Commit**

```bash
git add src/ForgeConflict.App
git commit -m "Add elevated WPF shell wiring log, exception guard and self-test"
```

---

### Task 8 : Publication portable

**Files:**
- Modify: `src/ForgeConflict.App/ForgeConflict.App.csproj`
- Create: `build.ps1`

- [ ] **Step 1 : Déclarer les options de publication**

Modify `src/ForgeConflict.App/ForgeConflict.App.csproj` — ajouter dans le `<PropertyGroup>` existant :

```xml
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <SelfContained>true</SelfContained>
    <PublishSingleFile>true</PublishSingleFile>
    <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
    <EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>
    <!-- Le rognage casse WPF, qui repose massivement sur la reflexion. -->
    <PublishTrimmed>false</PublishTrimmed>
    <DebugType>embedded</DebugType>
```

- [ ] **Step 2 : Écrire le script de publication**

Create `build.ps1` :

```powershell
#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

# KILL : une instance en cours verrouille l'executable.
Get-Process -Name 'ForgeConflict' -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Host "arret du processus $($_.Id)"
    Stop-Process -Id $_.Id -Force -Confirm:$false
}

# CLEAN
foreach ($d in @('publish', 'src/ForgeConflict.App/bin', 'src/ForgeConflict.App/obj')) {
    if (Test-Path $d) { Remove-Item $d -Recurse -Force }
}

# BUILD
dotnet publish src/ForgeConflict.App/ForgeConflict.App.csproj -c Release -o publish
if ($LASTEXITCODE -ne 0) { throw "echec de la publication" }

# VERIFY
$exe = Join-Path $PSScriptRoot 'publish/ForgeConflict.exe'
if (-not (Test-Path $exe)) { throw "executable introuvable : $exe" }

$item = Get-Item $exe
$version = $item.VersionInfo.ProductVersion
$sizeMb = [math]::Round($item.Length / 1MB, 1)
$loose = (Get-ChildItem publish -File | Where-Object { $_.Name -ne 'ForgeConflict.exe' }).Count

Write-Host ""
Write-Host "ForgeConflict $version" -ForegroundColor Green
Write-Host "  $exe"
Write-Host "  $sizeMb Mo, $loose fichier(s) annexe(s)"
if ($loose -gt 0) {
    Write-Warning "la publication n'est pas un fichier unique"
}
```

- [ ] **Step 3 : Publier**

Run : `powershell -ExecutionPolicy Bypass -File .\build.ps1`
Expected : `ForgeConflict 0.1.0`, taille entre 130 et 180 Mo, `0 fichier(s) annexe(s)`.

- [ ] **Step 4 : Vérifier la portabilité dans la VM**

Ce lot n'écrit rien dans le système, mais la vérification de portabilité doit se faire sur une machine qui n'a jamais vu le SDK .NET — sinon le test ne prouve rien.

Run depuis l'hôte :

```powershell
# Restaurer la base propre puis demarrer la VM
Restore-VMSnapshot -VMName 'Nouvel ordinateur virtuel' -Name 'vm pret w11' -Confirm:$false
Start-VM -Name 'Nouvel ordinateur virtuel'
```

Copier `publish\ForgeConflict.exe` dans la VM par PowerShell Direct, puis l'exécuter dans la VM et confirmer :
1. l'exécutable démarre sans installer quoi que ce soit ;
2. l'invite UAC apparaît ;
3. « Verifier le socle » affiche `Administrateur : oui` ;
4. « Provoquer une exception » ne ferme pas l'application ;
5. `.logs\forgeconflict.log` est créé à côté de l'exe.

Consigner le résultat des cinq points. Un échec sur l'un d'eux bloque le lot.

- [ ] **Step 5 : Commit**

```bash
git add src/ForgeConflict.App/ForgeConflict.App.csproj build.ps1
git commit -m "Add self-contained single-file publish pipeline"
git push origin main
```

---

## Ce que ce lot ne fait pas

Aucun collecteur, aucune règle, aucune écriture système. `ForgeConflict.Native` reste vide : il existe pour que la frontière P/Invoke soit posée avant qu'un `DllImport` ne s'égare ailleurs.

## Suite

Lot 1B — inventaire : façades natives, les dix collecteurs du spec section 4, et l'orchestration parallèle produisant un `SnapshotEnvelope` complet. C'est le premier lot dont l'exécution se fait en VM.
