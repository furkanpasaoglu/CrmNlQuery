using Npgsql;
using CrmNlQuery.Infrastructure.Persistence.Postgres;

namespace CrmNlQuery.Tests.Infrastructure.Persistence.Postgres;

/// <summary>
/// Tests PostgreSQL readiness checks and transient error classification.
/// </summary>
public class PostgresReadinessTests
{
    /// <summary>
    /// Verifies that the probe fails fast when nothing listens on the port.
    /// </summary>
    [Fact]
    public async Task The_probe_fails_fast_when_nothing_listens_on_the_port()
    {
        var probe = PostgresReadiness.CreateProbe("Host=127.0.0.1;Port=1;Username=x;Database=x", connectTimeoutSeconds: 2);

        var error = await Record.ExceptionAsync(() => probe(CancellationToken.None));

        Assert.NotNull(error);
        Assert.True(PostgresReadiness.IsTransient(error!));
    }

    /// <summary>
    /// Verifies that a broken connection string is reported at once.
    /// </summary>
    [Fact]
    public void A_broken_connection_string_is_reported_at_once()
    {
        Assert.ThrowsAny<ArgumentException>(() => PostgresReadiness.CreateProbe("this is not a connection string"));
    }

    /// <summary>
    /// Verifies that permanent server errors are not retried.
    /// </summary>
    [Theory]
    [InlineData("28P01", false)]
    [InlineData("28000", false)]
    [InlineData("3D000", false)]
    [InlineData("57P03", true)]
    public void Permanent_server_errors_are_not_retried(string sqlState, bool transient)
    {
        var error = new PostgresException("message", "FATAL", "FATAL", sqlState);

        Assert.Equal(transient, PostgresReadiness.IsTransient(error));
    }
}
