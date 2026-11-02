using CrmNlQuery.Tests.Support;
using Microsoft.Extensions.Logging;
using CrmNlQuery.Application.Glossary.Services;
using CrmNlQuery.Domain.Glossary.Constants;
using CrmNlQuery.Domain.Glossary.Entities;
using CrmNlQuery.Infrastructure.Persistence.Memory.Knowledge;

namespace CrmNlQuery.Tests.Application.Glossary;

/// <summary>
/// Holds tests that a broken glossary entry is left out of prompts and warned about once an hour.
/// </summary>
public class GlossaryBrokenWarningTests
{
    private sealed class CaptureLogger : ILogger
    {
        public readonly List<LogLevel> Levels = new List<LogLevel>();
        /// <summary>
        /// Test logger stub whose scope creation returns nothing, since the tests only inspect captured warning messages.
        /// </summary>
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        /// <summary>
        /// Test logger stub that reports a level as enabled.
        /// </summary>
        public bool IsEnabled(LogLevel logLevel) => true;
        /// <summary>
        /// Test logger that captures log entries.
        /// </summary>
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Levels.Add(logLevel);
    }

    /// <summary>
    /// Verifies that aBroken entry warns once per hour not on every question and is left out of the matches.
    /// </summary>
    [Fact]
    public async Task ABrokenEntry_WarnsOncePerHour_NotOnEveryQuestion_AndIsLeftOutOfTheMatches()
    {
        var store = new MemoryGlossaryStore();
        await store.AddAsync(new GlossaryEntry
        {
            Kind = GlossaryKind.Filter, Entity = "account", Terms = new[] { new GlossaryTerm("eski terim") },
            Conditions = new[] { new GlossaryCondition("closedfield", "eq", new[] { "1" }) }, CreatedAt = DateTimeOffset.UtcNow, ModifiedAt = DateTimeOffset.UtcNow,
        });
        var now = new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);
        var logger = new CaptureLogger();
        var service = new GlossaryService(store, TestMetadata.Create(), () => true, null, logger, () => now);

        var first = await service.MatchAsync("eski terim ile listele");
        service.Invalidate();
        await service.MatchAsync("eski terim ile listele");
        now = now.AddMinutes(61);
        service.Invalidate();
        await service.MatchAsync("eski terim ile listele");

        Assert.Empty(first);
        Assert.Equal(2, logger.Levels.Count(l => l == LogLevel.Warning));
    }
}
