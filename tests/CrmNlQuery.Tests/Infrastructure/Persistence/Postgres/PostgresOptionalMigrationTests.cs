using Npgsql;
using CrmNlQuery.Infrastructure.Persistence.Postgres;
using CrmNlQuery.Tests.Infrastructure.Persistence.Postgres.History;

namespace CrmNlQuery.Tests.Infrastructure.Persistence.Postgres;

/// <summary>
/// Live tests of PostgreSQL migration mechanics, where an optional migration is rolled back and skipped without stopping the start.
/// </summary>
[Trait("Category", "Live")]
public class PostgresOptionalMigrationTests : IAsyncLifetime
{
    private readonly string _schema = "optmig_" + Guid.NewGuid().ToString("N").Substring(0, 12);
    private string Connection => PostgresHistoryStoreTests.ConnectionString + ";Search Path=" + _schema;

    private static readonly (int Version, string Sql) First = (1, "create table t1 (id int)");
    private static readonly (int Version, string Sql) Third = (3, "create table t3 (id int)");
    private static readonly (int Version, string Sql) MissingExtension = (2, "create extension if not exists nlquery_no_such_extension; create table t2 (id int)");
    private static readonly (int Version, string Sql) FixedSecond = (2, "create table t2 (id int)");

    /// <summary>
    /// Creates the temporary PostgreSQL schema used by the optional migration tests before they run.
    /// </summary>
    public async Task InitializeAsync()
    {
        await RunAsync(PostgresHistoryStoreTests.ConnectionString, $"create schema {_schema}");
    }

    /// <summary>
    /// Drops the temporary PostgreSQL schema and everything in it after the optional migration tests finish.
    /// </summary>
    public async Task DisposeAsync()
    {
        await RunAsync(PostgresHistoryStoreTests.ConnectionString, $"drop schema if exists {_schema} cascade");
    }

    private static async Task RunAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<List<string>> ScalarListAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(Connection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var list = new List<string>();
        while (await reader.ReadAsync())
        {
            list.Add(Convert.ToString(reader.GetValue(0))!);
        }
        return list;
    }

    private Task<List<string>> TablesAsync()
        => ScalarListAsync($"select table_name from information_schema.tables where table_schema = '{_schema}' and table_name like 't%' order by 1");

    private Task<List<string>> VersionsAsync()
        => ScalarListAsync("select version from schema_version order by 1");

    /// <summary>
    /// Verifies that a failing optional migration is rolled back and skipped while the others apply.
    /// </summary>
    [Fact]
    public async Task AFailingOptionalMigration_IsRolledBackAndSkipped_WhileTheOthersApply()
    {
        var result = await PostgresSchema.MigrateAsync(Connection, new[] { First, MissingExtension, Third }, new HashSet<int> { 2 });

        var skipped = Assert.Single(result.Skipped);
        Assert.Equal(2, skipped.Version);
        Assert.False(string.IsNullOrWhiteSpace(skipped.Reason));
        Assert.Equal(new[] { "t1", "t3" }, await TablesAsync());
        Assert.Equal(new[] { "1", "3" }, await VersionsAsync());
    }

    /// <summary>
    /// Verifies that a skipped optional migration is applied on a later run when it works again.
    /// </summary>
    [Fact]
    public async Task ASkippedOptionalMigration_IsAppliedOnALaterRun_WhenItWorksAgain()
    {
        await PostgresSchema.MigrateAsync(Connection, new[] { First, MissingExtension, Third }, new HashSet<int> { 2 });

        var again = await PostgresSchema.MigrateAsync(Connection, new[] { First, FixedSecond, Third }, new HashSet<int> { 2 });

        Assert.Empty(again.Skipped);
        Assert.Equal(new[] { "t1", "t2", "t3" }, await TablesAsync());
        Assert.Equal(new[] { "1", "2", "3" }, await VersionsAsync());
    }

    /// <summary>
    /// Verifies that a required migration that fails stops the start and is not recorded.
    /// </summary>
    [Fact]
    public async Task ARequiredMigrationThatFails_StopsTheStart_AndIsNotRecorded()
    {
        await Assert.ThrowsAsync<PostgresException>(() =>
            PostgresSchema.MigrateAsync(Connection, new[] { First, MissingExtension, Third }, new HashSet<int>()));

        Assert.Equal(new[] { "1" }, await VersionsAsync());
        Assert.Equal(new[] { "t1" }, await TablesAsync());
    }

    /// <summary>
    /// Verifies that a fully working run skips nothing and is repeatable.
    /// </summary>
    [Fact]
    public async Task AFullyWorkingRun_SkipsNothing_AndIsRepeatable()
    {
        var first = await PostgresSchema.MigrateAsync(Connection, new[] { First, FixedSecond, Third }, new HashSet<int> { 2 });
        var second = await PostgresSchema.MigrateAsync(Connection, new[] { First, FixedSecond, Third }, new HashSet<int> { 2 });

        Assert.Empty(first.Skipped);
        Assert.Empty(second.Skipped);
    }

    /// <summary>
    /// Verifies that the knowledge migration is the only optional one.
    /// </summary>
    [Fact]
    public void TheKnowledgeMigration_IsTheOnlyOptionalOne()
    {
        Assert.Equal(new[] { PostgresSchema.KnowledgeMigrationVersion }, PostgresSchema.OptionalVersions.OrderBy(v => v).ToArray());
        Assert.Equal(6, PostgresSchema.KnowledgeMigrationVersion);
    }
}
