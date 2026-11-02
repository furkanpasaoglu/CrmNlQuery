using Microsoft.Extensions.Logging;
using CrmNlQuery.Application.Ask.Services;

namespace CrmNlQuery.Eval.Services;

/// <summary>
/// Writes the ask pipeline's hidden error details, such as CRM query errors, to the console for eval.
/// </summary>
public sealed class EvalLogger : ILogger<AskPipeline>
{
    /// <summary>
    /// Begins a logging scope, which this logger does not support.
    /// </summary>
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    /// <summary>
    /// Reports whether the given log level is enabled.
    /// </summary>
    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

    /// <summary>
    /// Writes a log entry to the console.
    /// </summary>
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var text = formatter(state, exception);
        var detail = exception is null ? "" : " :: " + exception.Message.Replace("\n", " ");
        Console.WriteLine($"      [log {logLevel}] {text}{detail}");
    }
}
