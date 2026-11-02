using System.Text.Json;
using Npgsql;
using CrmNlQuery.Application.Glossary.Abstractions;
using CrmNlQuery.Application.Glossary.Exceptions;
using CrmNlQuery.Application.Glossary.Services;
using CrmNlQuery.Domain.Glossary.Entities;
using CrmNlQuery.Application.Glossary.Models;

namespace CrmNlQuery.Infrastructure.Persistence.Postgres.Knowledge;

/// <summary>
/// Stores business glossary entries, their terms and usage stats in PostgreSQL, with unique folded terms.
/// </summary>
public sealed class PostgresGlossaryStore : IGlossaryStore
{
    private readonly NpgsqlDataSource _db;

    private PostgresGlossaryStore(NpgsqlDataSource db)
    {
        _db = db;
    }

    /// <summary>
    /// Applies the PostgreSQL schema migrations for the connection string, then returns a glossary store backed by a new data source.
    /// </summary>
    public static async Task<PostgresGlossaryStore> CreateAsync(string connectionString, CancellationToken ct = default)
    {
        await PostgresSchema.MigrateAsync(connectionString, ct);
        return new PostgresGlossaryStore(NpgsqlDataSource.Create(connectionString));
    }

    public bool Persistent => true;

    private sealed record ConditionJson(string Attribute, string Operator, List<string> Values);

    private static string ConditionsToJson(IReadOnlyList<GlossaryCondition> conditions)
        => JsonSerializer.Serialize(conditions.Select(c => new ConditionJson(c.Attribute, c.Operator, c.Values.ToList())).ToList());

    private static IReadOnlyList<GlossaryCondition> ConditionsFromJson(string json)
        => (JsonSerializer.Deserialize<List<ConditionJson>>(json) ?? new List<ConditionJson>()).Select(c => new GlossaryCondition(c.Attribute, c.Operator, c.Values)).ToList();

    private const string Select = "select id, kind, entity, attribute, conditions, description, enabled, created_by, created_at, modified_by, modified_at from glossary_entries";

    private static GlossaryEntry Read(NpgsqlDataReader r, List<GlossaryTerm> terms) => new GlossaryEntry
    {
        Id = r.GetString(0),
        Kind = r.GetString(1),
        Entity = r.GetString(2),
        Attribute = r.IsDBNull(3) ? null : r.GetString(3),
        Conditions = ConditionsFromJson(r.GetString(4)),
        Description = r.GetString(5),
        Enabled = r.GetBoolean(6),
        CreatedBy = r.GetString(7),
        CreatedAt = r.GetFieldValue<DateTimeOffset>(8),
        ModifiedBy = r.GetString(9),
        ModifiedAt = r.GetFieldValue<DateTimeOffset>(10),
        Terms = terms,
    };

    private async Task<Dictionary<string, List<GlossaryTerm>>> TermsAsync(string? id, CancellationToken ct)
    {
        var map = new Dictionary<string, List<GlossaryTerm>>();
        await using var command = _db.CreateCommand("select entry_id, term, language from glossary_terms " + (id is null ? "" : "where entry_id = @id ") + "order by position");
        if (id is not null) { command.Parameters.AddWithValue("id", id); }
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var key = reader.GetString(0);
            if (!map.TryGetValue(key, out var list)) { map[key] = list = new List<GlossaryTerm>(); }
            list.Add(new GlossaryTerm(reader.GetString(1), reader.GetString(2)));
        }
        return map;
    }

    /// <summary>
    /// Reads all glossary entries with their terms from PostgreSQL, ordered by creation time then id.
    /// </summary>
    public async Task<IReadOnlyList<GlossaryEntry>> ListAsync(CancellationToken ct = default)
    {
        var terms = await TermsAsync(null, ct);
        var list = new List<GlossaryEntry>();
        await using var command = _db.CreateCommand(Select + " order by created_at, id");
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(Read(reader, terms.TryGetValue(reader.GetString(0), out var t) ? t : new List<GlossaryTerm>()));
        }
        return list;
    }

    /// <summary>
    /// Reads one glossary entry with its terms by id from PostgreSQL, returning null when it does not exist.
    /// </summary>
    public async Task<GlossaryEntry?> GetAsync(string id, CancellationToken ct = default)
    {
        var terms = await TermsAsync(id, ct);
        await using var command = _db.CreateCommand(Select + " where id = @id");
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Read(reader, terms.TryGetValue(id, out var t) ? t : new List<GlossaryTerm>()) : null;
    }

    private static void CheckOwnTerms(GlossaryEntry entry)
    {
        var dup = entry.Terms.Select(t => GlossaryTermKey.Of(t.Text)).GroupBy(k => k).FirstOrDefault(g => g.Count() > 1);
        if (dup is not null) { throw new GlossaryDuplicateTermException(dup.Key, entry.Id); }
    }

    private static async Task<GlossaryDuplicateTermException> DuplicateAsync(NpgsqlConnection connection, GlossaryEntry entry, CancellationToken ct)
    {
        foreach (var term in entry.Terms)
        {
            await using var command = new NpgsqlCommand("select entry_id from glossary_terms where folded_term = @key and entry_id <> @id", connection);
            command.Parameters.AddWithValue("key", GlossaryTermKey.Of(term.Text));
            command.Parameters.AddWithValue("id", entry.Id);
            var other = await command.ExecuteScalarAsync(ct) as string;
            if (other is not null) { return new GlossaryDuplicateTermException(term.Text, other); }
        }
        return new GlossaryDuplicateTermException(entry.Terms.FirstOrDefault()?.Text ?? "", entry.Id);
    }

    private static async Task InsertTermsAsync(NpgsqlConnection connection, NpgsqlTransaction tx, GlossaryEntry entry, CancellationToken ct)
    {
        var position = 0;
        foreach (var term in entry.Terms)
        {
            await using var command = new NpgsqlCommand("insert into glossary_terms (folded_term, entry_id, term, language, position) values (@key, @id, @term, @lang, @pos)", connection, tx);
            command.Parameters.AddWithValue("key", GlossaryTermKey.Of(term.Text));
            command.Parameters.AddWithValue("id", entry.Id);
            command.Parameters.AddWithValue("term", term.Text);
            command.Parameters.AddWithValue("lang", term.Language);
            command.Parameters.AddWithValue("pos", position++);
            await command.ExecuteNonQueryAsync(ct);
        }
    }

    private static void Fill(NpgsqlCommand command, GlossaryEntry entry)
    {
        command.Parameters.AddWithValue("id", entry.Id);
        command.Parameters.AddWithValue("kind", entry.Kind);
        command.Parameters.AddWithValue("entity", entry.Entity);
        command.Parameters.AddWithValue("attribute", (object?)entry.Attribute ?? DBNull.Value);
        command.Parameters.AddWithValue("conditions", ConditionsToJson(entry.Conditions));
        command.Parameters.AddWithValue("description", entry.Description);
        command.Parameters.AddWithValue("enabled", entry.Enabled);
        command.Parameters.AddWithValue("cb", entry.CreatedBy);
        command.Parameters.AddWithValue("ca", entry.CreatedAt);
        command.Parameters.AddWithValue("mb", entry.ModifiedBy);
        command.Parameters.AddWithValue("ma", entry.ModifiedAt);
    }

    /// <summary>
    /// Inserts a glossary entry and its terms in one PostgreSQL transaction after checking its own terms, failing on a duplicate term.
    /// </summary>
    public async Task AddAsync(GlossaryEntry entry, CancellationToken ct = default)
    {
        CheckOwnTerms(entry);
        await using var connection = await _db.OpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        try
        {
            await using (var command = new NpgsqlCommand(@"insert into glossary_entries (id, kind, entity, attribute, conditions, description, enabled, created_by, created_at, modified_by, modified_at)
                values (@id, @kind, @entity, @attribute, @conditions, @description, @enabled, @cb, @ca, @mb, @ma)", connection, tx))
            {
                Fill(command, entry);
                await command.ExecuteNonQueryAsync(ct);
            }
            await InsertTermsAsync(connection, tx, entry, ct);
            await tx.CommitAsync(ct);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await tx.RollbackAsync(ct);
            throw await DuplicateAsync(connection, entry, ct);
        }
    }

    /// <summary>
    /// Updates a glossary entry and replaces its terms in one PostgreSQL transaction, returning false when the entry does not exist.
    /// </summary>
    public async Task<bool> UpdateAsync(GlossaryEntry entry, CancellationToken ct = default)
    {
        CheckOwnTerms(entry);
        await using var connection = await _db.OpenConnectionAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        try
        {
            int changed;
            await using (var command = new NpgsqlCommand(@"update glossary_entries set kind = @kind, entity = @entity, attribute = @attribute, conditions = @conditions, description = @description,
                enabled = @enabled, created_by = @cb, created_at = @ca, modified_by = @mb, modified_at = @ma where id = @id", connection, tx))
            {
                Fill(command, entry);
                changed = await command.ExecuteNonQueryAsync(ct);
            }
            if (changed == 0) { await tx.RollbackAsync(ct); return false; }
            await using (var delete = new NpgsqlCommand("delete from glossary_terms where entry_id = @id", connection, tx))
            {
                delete.Parameters.AddWithValue("id", entry.Id);
                await delete.ExecuteNonQueryAsync(ct);
            }
            await InsertTermsAsync(connection, tx, entry, ct);
            await tx.CommitAsync(ct);
            return true;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await tx.RollbackAsync(ct);
            throw await DuplicateAsync(connection, entry, ct);
        }
    }

    /// <summary>
    /// Deletes a glossary entry from PostgreSQL and returns whether a row was removed.
    /// </summary>
    public async Task<bool> DeleteAsync(string id, CancellationToken ct = default)
    {
        await using var command = _db.CreateCommand("delete from glossary_entries where id = @id");
        command.Parameters.AddWithValue("id", id);
        return await command.ExecuteNonQueryAsync(ct) > 0;
    }

    /// <summary>
    /// Upserts the per-entry usage counters in PostgreSQL, adding applied or not-applied counts and the last not-applied time, only for existing entries.
    /// </summary>
    public async Task RecordAsync(string entryId, bool applied, DateTimeOffset at, CancellationToken ct = default)
    {
        await using var command = _db.CreateCommand(
            @"insert into glossary_stats (entry_id, applied, not_applied, last_not_applied_at)
              select id, @a, @n, @last from glossary_entries where id = @id
              on conflict (entry_id) do update set applied = glossary_stats.applied + @a, not_applied = glossary_stats.not_applied + @n,
                last_not_applied_at = coalesce(@last, glossary_stats.last_not_applied_at)");
        command.Parameters.AddWithValue("id", entryId);
        command.Parameters.AddWithValue("a", applied ? 1 : 0);
        command.Parameters.AddWithValue("n", applied ? 0 : 1);
        command.Parameters.Add(new NpgsqlParameter("last", NpgsqlTypes.NpgsqlDbType.TimestampTz) { Value = applied ? DBNull.Value : at });
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Reads the applied and not-applied counters of every glossary entry from the PostgreSQL stats table.
    /// </summary>
    public async Task<IReadOnlyList<GlossaryStats>> StatsAsync(CancellationToken ct = default)
    {
        var list = new List<GlossaryStats>();
        await using var command = _db.CreateCommand("select entry_id, applied, not_applied, last_not_applied_at from glossary_stats");
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new GlossaryStats(reader.GetString(0), reader.GetInt32(1), reader.GetInt32(2), reader.IsDBNull(3) ? null : reader.GetFieldValue<DateTimeOffset>(3)));
        }
        return list;
    }
}
