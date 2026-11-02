using Npgsql;
using CrmNlQuery.Application.DataPolicy.Abstractions;
using CrmNlQuery.Application.DataPolicy.Services;
using CrmNlQuery.Domain.Access.Entities;

namespace CrmNlQuery.Infrastructure.Persistence.Postgres.Access;

/// <summary>
/// Stores the data allowlist rules in PostgreSQL, saving table rules with an empty field name.
/// </summary>
public sealed class PostgresDataPolicyStore : IDataPolicyStore
{
    private readonly NpgsqlDataSource _db;

    private PostgresDataPolicyStore(NpgsqlDataSource db)
    {
        _db = db;
    }

    /// <summary>
    /// Creates the store and ensures its connection is ready.
    /// </summary>
    public static async Task<PostgresDataPolicyStore> CreateAsync(string connectionString, CancellationToken ct = default)
    {
        await PostgresSchema.MigrateAsync(connectionString, ct);
        return new PostgresDataPolicyStore(NpgsqlDataSource.Create(connectionString));
    }

    public bool Persistent => true;

    /// <summary>
    /// Reads all explicit table and field decisions from the PostgreSQL data_policy table, ordered by entity and attribute.
    /// </summary>
    public async Task<IReadOnlyList<DataPolicyRule>> ListAsync(CancellationToken ct = default)
    {
        var rules = new List<DataPolicyRule>();
        await using var command = _db.CreateCommand("select kind, entity, attribute, allowed, set_by, set_at from data_policy order by entity, attribute");
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var attribute = reader.GetString(2);
            rules.Add(new DataPolicyRule(reader.GetString(0), reader.GetString(1), attribute.Length == 0 ? null : attribute, reader.GetBoolean(3), reader.GetString(4), reader.GetFieldValue<DateTimeOffset>(5)));
        }
        return rules;
    }

    /// <summary>
    /// Inserts or updates a data policy rule.
    /// </summary>
    public async Task SetAsync(DataPolicyRule rule, CancellationToken ct = default)
    {
        var clean = DataPolicyRules.Normalize(rule);
        await using var command = _db.CreateCommand(
            @"insert into data_policy (kind, entity, attribute, allowed, set_by, set_at) values (@kind, @entity, @attribute, @allowed, @by, @at)
              on conflict (kind, entity, attribute) do update set allowed = @allowed, set_by = @by, set_at = @at");
        command.Parameters.AddWithValue("kind", clean.Kind);
        command.Parameters.AddWithValue("entity", clean.Entity);
        command.Parameters.AddWithValue("attribute", clean.Attribute ?? "");
        command.Parameters.AddWithValue("allowed", clean.Allowed);
        command.Parameters.AddWithValue("by", clean.SetBy);
        command.Parameters.AddWithValue("at", clean.SetAt);
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Deletes one data policy rule from PostgreSQL by kind, entity and attribute, returning whether a row was removed.
    /// </summary>
    public async Task<bool> RemoveAsync(string kind, string entity, string? attribute, CancellationToken ct = default)
    {
        await using var command = _db.CreateCommand("delete from data_policy where kind = @kind and entity = @entity and attribute = @attribute");
        command.Parameters.AddWithValue("kind", (kind ?? "").ToLowerInvariant());
        command.Parameters.AddWithValue("entity", (entity ?? "").Trim().ToLowerInvariant());
        command.Parameters.AddWithValue("attribute", string.IsNullOrWhiteSpace(attribute) ? "" : attribute.Trim().ToLowerInvariant());
        return await command.ExecuteNonQueryAsync(ct) > 0;
    }
}
