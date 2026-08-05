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
