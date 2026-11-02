using Npgsql;
using NpgsqlTypes;
using CrmNlQuery.Application.History.Abstractions;
using CrmNlQuery.Application.History.Services;
using CrmNlQuery.Domain.History.Entities;
using CrmNlQuery.Application.History.Models;

namespace CrmNlQuery.Infrastructure.Persistence.Postgres.History;

/// <summary>
/// PostgreSQL implementation of the history store, also paging questions for export, using parameterized SQL only and 32-character N-format GUIDs as outside ids.
/// </summary>
public sealed class PostgresHistoryStore : IHistoryStore, CrmNlQuery.Application.Export.Abstractions.IQuestionPager
{
    private const string Columns = "id, owner, question, entity, fetch_xml, created_at, title, starred, parent_id, conversation_id, steps, duration_ms, kind, payload";
    private const string ConversationColumns = "id, owner, title, pinned, created_at, updated_at";
    private readonly NpgsqlDataSource _db;

    private PostgresHistoryStore(NpgsqlDataSource db)
    {
        _db = db;
    }

    /// <summary>
    /// Runs the schema migrations and then returns a history store backed by a pooled PostgreSQL connection.
    /// </summary>
    public static async Task<PostgresHistoryStore> CreateAsync(string connectionString, CancellationToken ct = default)
    {
        await PostgresSchema.MigrateAsync(connectionString, ct);
        return new PostgresHistoryStore(NpgsqlDataSource.Create(connectionString));
    }

    /// <summary>
    /// Inserts a history entry (question, entity, FetchXML, optional parent, conversation, kind and payload) for the owner and returns its new id.
    /// </summary>
    public async Task<string> AddAsync(Guid owner, string question, string entity, string fetchXml, string? parentId = null, string? conversationId = null, CancellationToken ct = default, bool adminViewable = false,
        int? steps = null, int? durationMs = null, string? kind = null, string? payload = null)
    {
        await using var connection = await _db.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        await using var insert = new NpgsqlCommand(
            "insert into history (owner, question, entity, fetch_xml, parent_id, conversation_id, admin_viewable, steps, duration_ms, kind, payload) " +
            "values (@owner, @question, @entity, @fetch, @parent, (select id from conversations where id = @chat and owner = @owner), @viewable, @steps, @duration, @kind, @payload) " +
            "returning id, conversation_id, created_at", connection, transaction);
        insert.Parameters.AddWithValue("parent", parentId is not null && TryParseId(parentId, out var parentGuid) ? parentGuid : DBNull.Value);
        insert.Parameters.AddWithValue("chat", conversationId is not null && TryParseId(conversationId, out var chatGuid) ? chatGuid : DBNull.Value);
        insert.Parameters.AddWithValue("owner", owner);
        insert.Parameters.AddWithValue("question", question);
        insert.Parameters.AddWithValue("entity", entity);
        insert.Parameters.AddWithValue("fetch", fetchXml);
        insert.Parameters.AddWithValue("viewable", adminViewable);
        insert.Parameters.AddWithValue("steps", steps is int s ? s : DBNull.Value);
        insert.Parameters.AddWithValue("duration", durationMs is int d ? d : DBNull.Value);
        insert.Parameters.AddWithValue("kind", kind is null ? DBNull.Value : kind);
        insert.Parameters.AddWithValue("payload", payload is null ? DBNull.Value : payload);

        Guid id;
        Guid? chat;
        DateTimeOffset at;
        await using (var reader = await insert.ExecuteReaderAsync(ct))
        {
            await reader.ReadAsync(ct);
            id = reader.GetGuid(0);
            chat = reader.IsDBNull(1) ? null : reader.GetGuid(1);
            at = reader.GetFieldValue<DateTimeOffset>(2);
        }
        if (chat is not null)
        {
            await using var touch = new NpgsqlCommand("update conversations set updated_at = @at where id = @id", connection, transaction);
            touch.Parameters.AddWithValue("at", at.ToUniversalTime());
            touch.Parameters.AddWithValue("id", chat.Value);
            await touch.ExecuteNonQueryAsync(ct);
        }
        await transaction.CommitAsync(ct);
        return id.ToString("N");
    }

    /// <summary>
    /// Returns one keyset-paged batch of questions in a date range, oldest first, only those flagged admin-viewable at ask time, optionally for one user.
    /// </summary>
    public async Task<IReadOnlyList<CrmNlQuery.Application.Export.Models.QuestionPageItem>> PageQuestionsAsync(DateTimeOffset from, DateTimeOffset to, Guid? user,
        CrmNlQuery.Application.Export.Models.QuestionCursor? after, int size, CancellationToken ct = default)
    {
        Guid afterId = Guid.Empty;
        if (after is not null && !TryParseId(after.Id, out afterId))
        {
            return new List<CrmNlQuery.Application.Export.Models.QuestionPageItem>();
        }
        await using var command = _db.CreateCommand(
            @"select id, owner, created_at, question, entity from history
              where admin_viewable and created_at >= @from and created_at < @to
                and (@user::uuid is null or owner = @user::uuid)
                and (@afterAt::timestamptz is null or (created_at, id) > (@afterAt::timestamptz, @afterId::uuid))
              order by created_at, id limit @take");
        command.Parameters.AddWithValue("from", from.ToUniversalTime());
        command.Parameters.AddWithValue("to", to.ToUniversalTime());
        command.Parameters.AddWithValue("user", user is { } u ? u : DBNull.Value);
        command.Parameters.AddWithValue("afterAt", after is null ? DBNull.Value : after.At.ToUniversalTime());
        command.Parameters.AddWithValue("afterId", after is null ? DBNull.Value : afterId);
        command.Parameters.AddWithValue("take", Math.Max(1, size));
        var list = new List<CrmNlQuery.Application.Export.Models.QuestionPageItem>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new CrmNlQuery.Application.Export.Models.QuestionPageItem(reader.GetGuid(0).ToString("N"), reader.GetGuid(1), reader.GetFieldValue<DateTimeOffset>(2), reader.GetString(3), reader.GetString(4)));
        }
        return list;
    }

    /// <summary>
    /// Lists up to the requested number of the owner's questions that were flagged admin-viewable when asked; older unflagged entries never appear.
    /// </summary>
    public async Task<IReadOnlyList<ViewableQuestion>> ListViewableQuestionsAsync(Guid owner, int take, CancellationToken ct = default)
    {
        await using var command = _db.CreateCommand(
            "select created_at, question, entity from history where owner = @owner and admin_viewable order by created_at desc, id desc limit @take");
        command.Parameters.AddWithValue("owner", owner);
        command.Parameters.AddWithValue("take", Math.Max(1, take));
        var list = new List<ViewableQuestion>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new ViewableQuestion(reader.GetFieldValue<DateTimeOffset>(0), reader.GetString(1), reader.GetString(2)));
        }
        return list;
    }

    /// <summary>
    /// Fetches one history entry by id, restricted to the given owner, or returns null if missing or not theirs.
    /// </summary>
    public async Task<HistoryEntry?> GetAsync(Guid owner, string id, CancellationToken ct = default)
    {
        if (!TryParseId(id, out var guid))
        {
            return null;
        }
        await using var command = _db.CreateCommand($"select {Columns} from history where id = @id and owner = @owner");
        command.Parameters.AddWithValue("id", guid);
        command.Parameters.AddWithValue("owner", owner);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Read(reader) : null;
    }

    /// <summary>
    /// Deletes one history entry by id for the owner and reports whether a row was removed.
    /// </summary>
    public async Task<bool> DeleteAsync(Guid owner, string id, CancellationToken ct = default)
    {
        if (!TryParseId(id, out var guid))
        {
            return false;
        }
        await using var command = _db.CreateCommand("delete from history where id = @id and owner = @owner");
        command.Parameters.AddWithValue("id", guid);
        command.Parameters.AddWithValue("owner", owner);
        return await command.ExecuteNonQueryAsync(ct) > 0;
    }

    /// <summary>
    /// Deletes history entries created before the cutoff as part of retention cleanup and returns how many were removed.
    /// </summary>
    public async Task<int> PurgeOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default)
    {
        await using var connection = await _db.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using var entries = new NpgsqlCommand(
            "delete from history h where h.created_at < @cutoff " +
            "and not exists (select 1 from conversations c where c.id = h.conversation_id and c.pinned)", connection, transaction);
        entries.Parameters.AddWithValue("cutoff", cutoff.ToUniversalTime());
        var removed = await entries.ExecuteNonQueryAsync(ct);

        await using var chats = new NpgsqlCommand(
            "delete from conversations c where c.updated_at < @cutoff and not c.pinned " +
            "and not exists (select 1 from history h where h.conversation_id = c.id)", connection, transaction);
        chats.Parameters.AddWithValue("cutoff", cutoff.ToUniversalTime());
        await chats.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct);
        return removed;
    }

    /// <summary>
    /// Creates a new conversation with the given title for the owner and returns its id.
    /// </summary>
    public async Task<string> CreateConversationAsync(Guid owner, string title, CancellationToken ct = default)
    {
        await using var command = _db.CreateCommand("insert into conversations (owner, title) values (@owner, @title) returning id");
        command.Parameters.AddWithValue("owner", owner);
        command.Parameters.AddWithValue("title", title);
        var id = (Guid)(await command.ExecuteScalarAsync(ct))!;
        return id.ToString("N");
    }

    /// <summary>
    /// Fetches one conversation by id for the owner, or null if it does not exist or belongs to someone else.
    /// </summary>
    public async Task<Conversation?> GetConversationAsync(Guid owner, string id, CancellationToken ct = default)
    {
        if (!TryParseId(id, out var guid))
        {
            return null;
        }
        await using var command = _db.CreateCommand($"select {ConversationColumns} from conversations where id = @id and owner = @owner");
        command.Parameters.AddWithValue("id", guid);
        command.Parameters.AddWithValue("owner", owner);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadConversation(reader) : null;
    }

    /// <summary>
    /// Lists the owner's conversations one page at a time, optionally filtered by search text and pinned state, using a cursor.
    /// </summary>
    public async Task<ConversationPage> ListConversationsAsync(Guid owner, int take, string? cursor, string? search, bool? pinned, CancellationToken ct = default)
    {
        var size = Math.Max(0, take);
        var hasCursor = HistoryCursor.TryReadConversation(cursor, out var cursorPinned, out var cursorAt, out var cursorId)
            && TryParseId(cursorId, out _);
        TryParseId(cursorId, out var cursorGuid);

        await using var command = _db.CreateCommand(
            $"select {ConversationColumns} from conversations where owner = @owner " +
            "and (@pinned::boolean is null or pinned = @pinned) " +
            "and (@search = '' or position(lower(@search) in lower(title)) > 0) " +
            "and (not @has_cursor or (pinned, updated_at, id) < (@cursor_pinned, @cursor_at, @cursor_id)) " +
            "order by pinned desc, updated_at desc, id desc limit @limit");
        command.Parameters.AddWithValue("owner", owner);
        command.Parameters.Add(new NpgsqlParameter("pinned", NpgsqlDbType.Boolean) { Value = (object?)pinned ?? DBNull.Value });
        command.Parameters.AddWithValue("search", (search ?? "").Trim());
        command.Parameters.AddWithValue("has_cursor", hasCursor);
        command.Parameters.AddWithValue("cursor_pinned", hasCursor && cursorPinned);
        command.Parameters.AddWithValue("cursor_at", hasCursor ? cursorAt.ToUniversalTime() : DateTimeOffset.UnixEpoch);
        command.Parameters.AddWithValue("cursor_id", hasCursor ? cursorGuid : Guid.Empty);
        command.Parameters.AddWithValue("limit", size + 1);

        var items = new List<Conversation>();
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                items.Add(ReadConversation(reader));
            }
        }
        var more = items.Count > size;
        var page = items.Take(size).ToList();
        var next = more && page.Count > 0 ? HistoryCursor.ForConversation(page[page.Count - 1].Pinned, page[page.Count - 1].UpdatedAt, page[page.Count - 1].Id) : null;
        return new ConversationPage(page, next);
    }

    /// <summary>
    /// Renames and/or pins or unpins a conversation owned by the user and reports whether it was found.
    /// </summary>
    public async Task<bool> UpdateConversationAsync(Guid owner, string id, string? title, bool? pinned, CancellationToken ct = default)
    {
        if (!TryParseId(id, out var guid))
        {
            return false;
        }
        await using var command = _db.CreateCommand(
            "update conversations set title = coalesce(@title, title), pinned = coalesce(@pinned, pinned) where id = @id and owner = @owner");
        command.Parameters.Add(new NpgsqlParameter("title", NpgsqlDbType.Text) { Value = (object?)title ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("pinned", NpgsqlDbType.Boolean) { Value = (object?)pinned ?? DBNull.Value });
        command.Parameters.AddWithValue("id", guid);
        command.Parameters.AddWithValue("owner", owner);
        return await command.ExecuteNonQueryAsync(ct) > 0;
    }

    /// <summary>
    /// Deletes one conversation (and its messages) owned by the user and reports whether it existed.
    /// </summary>
    public async Task<bool> DeleteConversationAsync(Guid owner, string id, CancellationToken ct = default)
    {
        if (!TryParseId(id, out var guid))
        {
            return false;
        }
        await using var command = _db.CreateCommand("delete from conversations where id = @id and owner = @owner");
        command.Parameters.AddWithValue("id", guid);
        command.Parameters.AddWithValue("owner", owner);
        return await command.ExecuteNonQueryAsync(ct) > 0;
    }

    /// <summary>
    /// Deletes all of the owner's conversations, skipping pinned ones unless includePinned is true, and returns the number deleted.
    /// </summary>
    public async Task<int> DeleteAllConversationsAsync(Guid owner, bool includePinned, CancellationToken ct = default)
    {
        await using var connection = await _db.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using var chats = new NpgsqlCommand("delete from conversations where owner = @owner and (@all or not pinned)", connection, transaction);
        chats.Parameters.AddWithValue("owner", owner);
        chats.Parameters.AddWithValue("all", includePinned);
        var removed = await chats.ExecuteNonQueryAsync(ct);

        await using var legacy = new NpgsqlCommand("delete from history where owner = @owner and conversation_id is null", connection, transaction);
        legacy.Parameters.AddWithValue("owner", owner);
        await legacy.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct);
        return removed;
    }

    /// <summary>
    /// Returns the owner's conversation counts used by the chat list and the delete-all confirmation.
    /// </summary>
    public async Task<ConversationCounts> CountConversationsAsync(Guid owner, CancellationToken ct = default)
    {
        await using var command = _db.CreateCommand("select count(*)::int, (count(*) filter (where pinned))::int from conversations where owner = @owner");
        command.Parameters.AddWithValue("owner", owner);
        await using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return new ConversationCounts(reader.GetInt32(0), reader.GetInt32(1));
    }

    /// <summary>
    /// Returns one page of a conversation's messages for its owner, paged by cursor.
    /// </summary>
    public async Task<MessagePage> ListMessagesAsync(Guid owner, string conversationId, int take, string? cursor, CancellationToken ct = default)
    {
        var size = Math.Max(0, take);
        if (!TryParseId(conversationId, out var chat))
        {
            return new MessagePage(new List<HistoryEntry>(), null);
        }
        var hasCursor = HistoryCursor.TryReadMessage(cursor, out var cursorAt, out var cursorId) && TryParseId(cursorId, out _);
        TryParseId(cursorId, out var cursorGuid);

        await using var command = _db.CreateCommand(
            $"select {Columns} from history where owner = @owner and conversation_id = @chat " +
            "and (not @has_cursor or (created_at, id) < (@cursor_at, @cursor_id)) " +
            "order by created_at desc, id desc limit @limit");
        command.Parameters.AddWithValue("owner", owner);
        command.Parameters.AddWithValue("chat", chat);
        command.Parameters.AddWithValue("has_cursor", hasCursor);
        command.Parameters.AddWithValue("cursor_at", hasCursor ? cursorAt.ToUniversalTime() : DateTimeOffset.UnixEpoch);
        command.Parameters.AddWithValue("cursor_id", hasCursor ? cursorGuid : Guid.Empty);
        command.Parameters.AddWithValue("limit", size + 1);

        var items = new List<HistoryEntry>();
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                items.Add(Read(reader));
            }
        }
        var more = items.Count > size;
        var page = items.Take(size).ToList();
        var next = more && page.Count > 0 ? HistoryCursor.ForMessage(page[page.Count - 1].CreatedAt, page[page.Count - 1].Id) : null;
        page.Reverse();
        return new MessagePage(page, next);
    }

    /// <summary>
    /// Returns the most recent history entry of the owner's conversation, or null if the conversation is empty or not theirs.
    /// </summary>
    public async Task<HistoryEntry?> LastEntryAsync(Guid owner, string conversationId, CancellationToken ct = default)
    {
        if (!TryParseId(conversationId, out var chat))
        {
            return null;
        }
        await using var command = _db.CreateCommand(
            $"select {Columns} from history where owner = @owner and conversation_id = @chat order by created_at desc, id desc limit 1");
        command.Parameters.AddWithValue("owner", owner);
        command.Parameters.AddWithValue("chat", chat);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Read(reader) : null;
    }

    private static bool TryParseId(string? id, out Guid guid)
    {
        guid = Guid.Empty;
        return id is { Length: 32 } && Guid.TryParseExact(id, "N", out guid);
    }

    private static HistoryEntry Read(NpgsqlDataReader r)
    {
        return new HistoryEntry(
            r.GetGuid(0).ToString("N"),
            r.GetGuid(1),
            r.GetString(2),
            r.GetString(3),
            r.GetString(4),
            r.GetFieldValue<DateTimeOffset>(5),
            r.IsDBNull(6) ? null : r.GetString(6),
            r.GetBoolean(7),
            r.IsDBNull(8) ? null : r.GetGuid(8).ToString("N"),
            r.IsDBNull(9) ? null : r.GetGuid(9).ToString("N"),
            r.IsDBNull(10) ? null : r.GetInt32(10),
            r.IsDBNull(11) ? null : r.GetInt32(11),
            r.IsDBNull(12) ? null : r.GetString(12),
            r.IsDBNull(13) ? null : r.GetString(13));
    }

    private static Conversation ReadConversation(NpgsqlDataReader r)
    {
        return new Conversation(
            r.GetGuid(0).ToString("N"),
            r.GetGuid(1),
            r.GetString(2),
            r.GetBoolean(3),
            r.GetFieldValue<DateTimeOffset>(4),
            r.GetFieldValue<DateTimeOffset>(5));
    }
}
