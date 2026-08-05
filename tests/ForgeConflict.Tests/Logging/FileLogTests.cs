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
