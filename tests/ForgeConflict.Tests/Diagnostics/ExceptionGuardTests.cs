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

        guard.HandleDomain(exceptionObject: "objet non type", isTerminating: true);

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
