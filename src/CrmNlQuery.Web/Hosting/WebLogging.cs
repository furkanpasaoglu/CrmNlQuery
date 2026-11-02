using Serilog;
using Serilog.Events;
using CrmNlQuery.Application.Observability.Models;

namespace CrmNlQuery.Web.Hosting;

/// <summary>
/// Configures logging to the console and a daily rolling file kept 14 days, never writing row data or question text.
/// </summary>
public static class WebLogging
{
    /// <summary>
    /// Adds the log sink immediately and connects the store once composition finishes, queuing earlier events until then.
    /// </summary>
    public static LogStoreSink Configure(WebApplicationBuilder builder)
    {
        var logSink = new LogStoreSink(TimeSpan.FromSeconds(1));
        var databaseLogLevel = (LogEventLevel)LogLevels.Parse(builder.Configuration["Logging:Database:MinimumLevel"], LogLevels.Warning);
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .WriteTo.Console()
            .WriteTo.Sink(logSink, databaseLogLevel)
            .WriteTo.File(builder.Configuration["Logging:File:Path"] ?? "logs/nlquery-.log", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14)
            .CreateLogger();
        builder.Host.UseSerilog();
        return logSink;
    }
}
