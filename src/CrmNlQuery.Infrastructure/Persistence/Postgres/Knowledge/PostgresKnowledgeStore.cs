using System.Globalization;
using System.Text;
using Npgsql;
using CrmNlQuery.Application.Knowledge.Abstractions;
using CrmNlQuery.Application.Knowledge.Services;
using CrmNlQuery.Domain.Knowledge.Entities;

namespace CrmNlQuery.Infrastructure.Persistence.Postgres.Knowledge;

/// <summary>
/// Stores knowledge documents and chunks in PostgreSQL with pgvector, always passing user text as SQL parameters.
/// </summary>
public sealed class PostgresKnowledgeStore : IKnowledgeStore
{
    private readonly NpgsqlDataSource _db;

    private PostgresKnowledgeStore(NpgsqlDataSource db)
    {
        _db = db;
    }

    /// <summary>
    /// Creates the store and applies the knowledge-base migration, throwing if pgvector cannot be set up.
    /// </summary>
    public static async Task<PostgresKnowledgeStore> CreateAsync(string connectionString, CancellationToken ct = default)
    {
        var result = await PostgresSchema.MigrateAsync(connectionString, ct);
        var skipped = result.Skipped.FirstOrDefault(s => s.Version == PostgresSchema.KnowledgeMigrationVersion);
        if (skipped is not null)
        {
            throw new InvalidOperationException("The knowledge base schema could not be created (is the pgvector extension installed?): " + skipped.Reason);
        }
        return new PostgresKnowledgeStore(NpgsqlDataSource.Create(connectionString));
    }

    /// <summary>
    /// Creates the store at startup, or returns an unavailable store with a reason when pgvector is missing so the service still starts.
    /// </summary>
    public static async Task<IKnowledgeStore> CreateOrUnavailableAsync(string connectionString, Action<string>? onLog = null, CancellationToken ct = default)
    {
        var result = await PostgresSchema.MigrateAsync(connectionString, ct);
        var skipped = result.Skipped.FirstOrDefault(s => s.Version == PostgresSchema.KnowledgeMigrationVersion);
        if (skipped is not null)
        {
            onLog?.Invoke(skipped.Reason);
            return new UnavailableKnowledgeStore("the pgvector extension is not installed in PostgreSQL (or the database user may not create it)");
        }
        return new PostgresKnowledgeStore(NpgsqlDataSource.Create(connectionString));
    }

    /// <summary>
    /// Reads all knowledge documents from the PostgreSQL knowledge_documents table, most recently updated first.
    /// </summary>
    public async Task<IReadOnlyList<KnowledgeDocument>> ListAsync(CancellationToken ct = default)
    {
        await using var command = _db.CreateCommand("select id, title, body, enabled, updated_by, updated_at from knowledge_documents order by updated_at desc");
        await using var reader = await command.ExecuteReaderAsync(ct);
        var list = new List<KnowledgeDocument>();
        while (await reader.ReadAsync(ct))
        {
            list.Add(ReadDocument(reader));
        }
        return list;
    }

    /// <summary>
    /// Reads one knowledge document by id from PostgreSQL, returning null when no such document exists.
    /// </summary>
    public async Task<KnowledgeDocument?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var command = _db.CreateCommand("select id, title, body, enabled, updated_by, updated_at from knowledge_documents where id = @id");
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadDocument(reader) : null;
    }

    /// <summary>
    /// Upserts a knowledge document and replaces its chunks with the given embedded chunks inside a single PostgreSQL transaction.
    /// </summary>
    public async Task SaveAsync(KnowledgeDocument document, IReadOnlyList<KnowledgeChunk> chunks, CancellationToken ct = default)
    {
        await using var connection = await _db.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        await using (var upsert = new NpgsqlCommand(
            @"insert into knowledge_documents (id, title, body, enabled, updated_by, updated_at) values (@id, @title, @body, @enabled, @by, @at)
              on conflict (id) do update set title = @title, body = @body, enabled = @enabled, updated_by = @by, updated_at = @at",
            connection, transaction))
        {
            upsert.Parameters.AddWithValue("id", document.Id);
            upsert.Parameters.AddWithValue("title", document.Title);
            upsert.Parameters.AddWithValue("body", document.Body);
            upsert.Parameters.AddWithValue("enabled", document.Enabled);
            upsert.Parameters.AddWithValue("by", document.UpdatedBy);
            upsert.Parameters.AddWithValue("at", document.UpdatedAt.ToUniversalTime());
            await upsert.ExecuteNonQueryAsync(ct);
        }

        await using (var clear = new NpgsqlCommand("delete from knowledge_chunks where document_id = @id", connection, transaction))
        {
            clear.Parameters.AddWithValue("id", document.Id);
            await clear.ExecuteNonQueryAsync(ct);
        }

        foreach (var chunk in chunks)
        {
            await using var insert = new NpgsqlCommand(
                "insert into knowledge_chunks (document_id, chunk_index, text, embedding) values (@id, @index, @text, @embedding::vector)",
                connection, transaction);
            insert.Parameters.AddWithValue("id", document.Id);
            insert.Parameters.AddWithValue("index", chunk.Index);
            insert.Parameters.AddWithValue("text", chunk.Text);
            insert.Parameters.AddWithValue("embedding", VectorText(chunk.Embedding));
            await insert.ExecuteNonQueryAsync(ct);
        }
        await transaction.CommitAsync(ct);
    }

    /// <summary>
    /// Updates only the enabled flag, updater and timestamp of a knowledge document in PostgreSQL.
    /// </summary>
    public async Task SetEnabledAsync(Guid id, bool enabled, string actor, DateTimeOffset at, CancellationToken ct = default)
    {
        await using var command = _db.CreateCommand("update knowledge_documents set enabled = @enabled, updated_by = @by, updated_at = @at where id = @id");
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("enabled", enabled);
        command.Parameters.AddWithValue("by", actor);
        command.Parameters.AddWithValue("at", at.ToUniversalTime());
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Deletes a knowledge document from PostgreSQL; its chunks are removed by the on-delete-cascade foreign key.
    /// </summary>
    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var command = _db.CreateCommand("delete from knowledge_documents where id = @id");
        command.Parameters.AddWithValue("id", id);
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Finds the knowledge chunks nearest to the query embedding using pgvector cosine distance, returning up to the requested number with similarity scores.
    /// </summary>
    public async Task<IReadOnlyList<KnowledgeSnippet>> SearchByVectorAsync(float[] embedding, int take, CancellationToken ct = default)
    {
        await using var command = _db.CreateCommand(
            @"select c.document_id, c.chunk_index, d.title, c.text, 1 - (c.embedding <=> @q::vector) as score
              from knowledge_chunks c join knowledge_documents d on d.id = c.document_id
              where d.enabled
              order by c.embedding <=> @q::vector
              limit @take");
        command.Parameters.AddWithValue("q", VectorText(embedding));
        command.Parameters.AddWithValue("take", take);
        return await ReadSnippets(command, "vector", ct);
    }

    /// <summary>
    /// Finds knowledge chunks by PostgreSQL full-text search matching any question word, passing the text only as a parameter, and returns nothing for an empty query.
    /// </summary>
    public async Task<IReadOnlyList<KnowledgeSnippet>> SearchByTextAsync(string question, int take, CancellationToken ct = default)
    {
        var query = OrQuery(question);
        if (query.Length == 0)
        {
            return new List<KnowledgeSnippet>();
        }
        await using var command = _db.CreateCommand(
            @"with q as (select websearch_to_tsquery('simple', @q) || websearch_to_tsquery('turkish', @q) as query)
              select c.document_id, c.chunk_index, d.title, c.text, ts_rank(c.tsv, q.query)::float8 as score
              from knowledge_chunks c join knowledge_documents d on d.id = c.document_id, q
              where d.enabled and c.tsv @@ q.query
              order by score desc
              limit @take");
        command.Parameters.AddWithValue("q", query);
        command.Parameters.AddWithValue("take", take);
        return await ReadSnippets(command, "text", ct);
    }

    /// <summary>
    /// Turns the distinctive words of a question into an OR text-search query, dropping command and question words.
    /// </summary>
    public static string OrQuery(string question)
    {
        return string.Join(" or ", SearchWords.Extract(question).Where(w => !string.Equals(w, "or", StringComparison.OrdinalIgnoreCase)));
    }

    private static async Task<IReadOnlyList<KnowledgeSnippet>> ReadSnippets(NpgsqlCommand command, string source, CancellationToken ct)
    {
        await using var reader = await command.ExecuteReaderAsync(ct);
        var list = new List<KnowledgeSnippet>();
        while (await reader.ReadAsync(ct))
        {
            list.Add(new KnowledgeSnippet(reader.GetGuid(0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3), reader.GetDouble(4), source));
        }
        return list;
    }

    private static KnowledgeDocument ReadDocument(NpgsqlDataReader reader)
    {
        return new KnowledgeDocument(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetBoolean(3), reader.GetString(4),
            new DateTimeOffset(reader.GetDateTime(5), TimeSpan.Zero));
    }

    private static string VectorText(float[] values)
    {
        var sb = new StringBuilder("[");
        for (var i = 0; i < values.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }
            sb.Append(values[i].ToString("R", CultureInfo.InvariantCulture));
        }
        return sb.Append(']').ToString();
    }
}
