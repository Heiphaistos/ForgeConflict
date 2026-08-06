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
