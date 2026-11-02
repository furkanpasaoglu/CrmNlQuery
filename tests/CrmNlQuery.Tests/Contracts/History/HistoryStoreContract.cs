using CrmNlQuery.Application.History.Abstractions;
using CrmNlQuery.Application.History.Models;

namespace CrmNlQuery.Tests.Contracts.History;

/// <summary>
/// Defines the shared behaviors every history store implementation must pass.
/// </summary>
public abstract class HistoryStoreContract
{
    protected abstract IHistoryStore CreateStore();

    private const string Fetch = "<fetch top=\"500\"><entity name=\"product\" /></fetch>";

    /// <summary>
    /// Verifies that add then get returns the entry for its owner.
    /// </summary>
    [Fact]
    public async Task Add_ThenGet_ReturnsTheEntry_ForItsOwner()
    {
        var store = CreateStore();
        var owner = Guid.NewGuid();
        var id = await store.AddAsync(owner, "Bütün productları getir", "product", Fetch);

        var entry = await store.GetAsync(owner, id);

        Assert.NotNull(entry);
        Assert.Equal(id, entry!.Id);
        Assert.Equal(owner, entry.Owner);
        Assert.Equal("Bütün productları getir", entry.Question);
        Assert.Equal("product", entry.Entity);
        Assert.Equal(Fetch, entry.FetchXml);
        Assert.False(entry.Starred);
        Assert.Null(entry.Title);
        Assert.True(Math.Abs((DateTimeOffset.UtcNow - entry.CreatedAt).TotalMinutes) < 5);
    }

    /// <summary>
    /// Verifies that the kind and the technical meta are stored and returned and default to null.
    /// </summary>
    [Fact]
    public async Task TheKindAndTheTechnicalMeta_AreStoredAndReturned_AndDefaultToNull()
    {
        var store = CreateStore();
        var owner = Guid.NewGuid();

        var plain = await store.AddAsync(owner, "q", "product", Fetch);
        var summary = await store.AddAsync(owner, "özetle", "account", Fetch, steps: 1, durationMs: 250, kind: "summary");

        var plainEntry = (await store.GetAsync(owner, plain))!;
        var summaryEntry = (await store.GetAsync(owner, summary))!;
        Assert.Null(plainEntry.Kind);
        Assert.Equal("summary", summaryEntry.Kind);
        Assert.Equal(1, summaryEntry.Steps);
        Assert.Equal(250, summaryEntry.DurationMs);
    }

    /// <summary>
    /// Verifies that the payload is stored and returned and defaults to null.
    /// </summary>
    [Fact]
    public async Task ThePayload_IsStoredAndReturned_AndDefaultsToNull()
    {
        var store = CreateStore();
        var owner = Guid.NewGuid();

        var plain = await store.AddAsync(owner, "q", "product", Fetch);
        var draft = await store.AddAsync(owner, "yaz", "account", Fetch, kind: "draft", payload: "{\"subject\":\"Merhaba\"}");

        Assert.Null((await store.GetAsync(owner, plain))!.Payload);
        var entry = (await store.GetAsync(owner, draft))!;
        Assert.Equal("draft", entry.Kind);
        Assert.Equal("{\"subject\":\"Merhaba\"}", entry.Payload);
    }

    /// <summary>
    /// Verifies that IDs are random hex and unique.
    /// </summary>
    [Fact]
    public async Task Ids_AreRandomHex_AndUnique()
    {
        var store = CreateStore();
        var owner = Guid.NewGuid();
        var a = await store.AddAsync(owner, "q", "product", Fetch);
        var b = await store.AddAsync(owner, "q", "product", Fetch);
        Assert.NotEqual(a, b);
        Assert.Equal(32, a.Length);
        Assert.All(a, c => Assert.True(Uri.IsHexDigit(c) && !char.IsUpper(c)));
    }

    /// <summary>
    /// Verifies that another user sees nothing same as unknown or bad IDs.
    /// </summary>
    [Fact]
    public async Task AnotherUser_SeesNothing_SameAsUnknownOrBadIds()
    {
        var store = CreateStore();
        var id = await store.AddAsync(Guid.NewGuid(), "q", "product", Fetch);
        var stranger = Guid.NewGuid();

        Assert.Null(await store.GetAsync(stranger, id));
        Assert.Null(await store.GetAsync(stranger, Guid.NewGuid().ToString("N")));
        Assert.Null(await store.GetAsync(stranger, "nope"));
        Assert.Null(await store.GetAsync(stranger, ""));
        Assert.Null(await store.GetAsync(stranger, "../../etc/passwd"));
    }





    /// <summary>
    /// Verifies that delete removes the entry only for its owner.
    /// </summary>
    [Fact]
    public async Task Delete_RemovesTheEntry_OnlyForItsOwner()
    {
        var store = CreateStore();
        var owner = Guid.NewGuid();
        var id = await store.AddAsync(owner, "q", "product", Fetch);

        Assert.False(await store.DeleteAsync(Guid.NewGuid(), id));
        Assert.NotNull(await store.GetAsync(owner, id));

        Assert.True(await store.DeleteAsync(owner, id));
        Assert.Null(await store.GetAsync(owner, id));
        Assert.False(await store.DeleteAsync(owner, id));
    }

    /// <summary>
    /// Verifies that questions with quotes and unicode are stored exactly.
    /// </summary>
    [Fact]
    public async Task QuestionsWithQuotesAndUnicode_AreStoredExactly()
    {
        var store = CreateStore();
        var owner = Guid.NewGuid();
        var question = "O'Brien \"Ltd\"; DROP TABLE x; -- Şırnak İstanbul ğüöç <b>x</b>";
        var id = await store.AddAsync(owner, question, "account", Fetch);
        Assert.Equal(question, (await store.GetAsync(owner, id))!.Question);
    }

    /// <summary>
    /// Verifies that aParent id is stored and read and an entry without one has none.
    /// </summary>
    [Fact]
    public async Task AParentId_IsStoredAndRead_AndAnEntryWithoutOneHasNone()
    {
        var store = CreateStore();
        var owner = Guid.NewGuid();
        var parent = await store.AddAsync(owner, "first", "product", Fetch);
        var child = await store.AddAsync(owner, "second", "product", Fetch, parent);
        var alone = await store.AddAsync(owner, "third", "product", Fetch);

        Assert.Equal(parent, (await store.GetAsync(owner, child))!.ParentId);
        Assert.Null((await store.GetAsync(owner, parent))!.ParentId);
        Assert.Null((await store.GetAsync(owner, alone))!.ParentId);
    }

    /// <summary>
    /// Verifies that deleting the parent does not break the child.
    /// </summary>
    [Fact]
    public async Task DeletingTheParent_DoesNotBreakTheChild()
    {
        var store = CreateStore();
        var owner = Guid.NewGuid();
        var parent = await store.AddAsync(owner, "first", "product", Fetch);
        var child = await store.AddAsync(owner, "second", "product", Fetch, parent);

        await store.DeleteAsync(owner, parent);

        Assert.NotNull(await store.GetAsync(owner, child));
    }

    /// <summary>
    /// Verifies that aBroken parent id is ignored and the entry is still saved.
    /// </summary>
    [Fact]
    public async Task ABrokenParentId_IsIgnored_AndTheEntryIsStillSaved()
    {
        var store = CreateStore();
        var owner = Guid.NewGuid();

        var id = await store.AddAsync(owner, "q", "product", Fetch, "not-a-guid");

        Assert.NotNull(await store.GetAsync(owner, id));
    }

    /// <summary>
    /// Verifies that aCreated conversation is readable only by its owner.
    /// </summary>
    [Fact]
    public async Task ACreatedConversation_IsReadableOnlyByItsOwner()
    {
        var store = CreateStore();
        var owner = Guid.NewGuid();

        var id = await store.CreateConversationAsync(owner, "Londra'daki firmalar");

        var chat = await store.GetConversationAsync(owner, id);
        Assert.NotNull(chat);
        Assert.Equal(id, chat!.Id);
        Assert.Equal(owner, chat.Owner);
        Assert.Equal("Londra'daki firmalar", chat.Title);
        Assert.False(chat.Pinned);
        Assert.Equal(32, id.Length);
        Assert.Null(await store.GetConversationAsync(Guid.NewGuid(), id));
        Assert.Null(await store.GetConversationAsync(owner, Guid.NewGuid().ToString("N")));
        Assert.Null(await store.GetConversationAsync(owner, "nope"));
        Assert.Null(await store.GetConversationAsync(owner, ""));
    }

    /// <summary>
    /// Verifies that an entry added to aConversation knows it and touches the conversation.
    /// </summary>
    [Fact]
    public async Task AnEntryAddedToAConversation_KnowsIt_AndTouchesTheConversation()
    {
        var store = CreateStore();
        var owner = Guid.NewGuid();
        var chat = await store.CreateConversationAsync(owner, "chat");
        var before = (await store.GetConversationAsync(owner, chat))!.UpdatedAt;
        await Task.Delay(20);

        var entry = await store.AddAsync(owner, "q", "product", Fetch, null, chat);

        Assert.Equal(chat, (await store.GetAsync(owner, entry))!.ConversationId);
        Assert.True((await store.GetConversationAsync(owner, chat))!.UpdatedAt > before);
    }

    /// <summary>
    /// Verifies that an entry cannot be added to someone elses conversation.
    /// </summary>
    [Fact]
    public async Task AnEntry_CannotBeAddedToSomeoneElsesConversation()
    {
        var store = CreateStore();
        var owner = Guid.NewGuid();
        var stranger = Guid.NewGuid();
        var chat = await store.CreateConversationAsync(owner, "private");

        var entry = await store.AddAsync(stranger, "q", "product", Fetch, null, chat);

        Assert.Null((await store.GetAsync(stranger, entry))!.ConversationId);
        Assert.Empty((await store.ListMessagesAsync(owner, chat, 10, null)).Items);
    }

    /// <summary>
    /// Verifies that conversations are listed pinned first then newest updated owner only.
    /// </summary>
    [Fact]
    public async Task Conversations_AreListedPinnedFirst_ThenNewestUpdated_OwnerOnly()
    {
        var store = CreateStore();
        var owner = Guid.NewGuid();
        var a = await store.CreateConversationAsync(owner, "a");
        await Task.Delay(20);
        var b = await store.CreateConversationAsync(owner, "b");
        await Task.Delay(20);
        var c = await store.CreateConversationAsync(owner, "c");
        await store.CreateConversationAsync(Guid.NewGuid(), "not yours");
        await store.UpdateConversationAsync(owner, a, null, true);

        var all = await store.ListConversationsAsync(owner, 10, null, null, null);

        Assert.Equal(new[] { a, c, b }, all.Items.Select(x => x.Id).ToArray());
        Assert.Null(all.NextCursor);
        Assert.Equal(new[] { a }, (await store.ListConversationsAsync(owner, 10, null, null, true)).Items.Select(x => x.Id).ToArray());
        Assert.Equal(new[] { c, b }, (await store.ListConversationsAsync(owner, 10, null, null, false)).Items.Select(x => x.Id).ToArray());
        Assert.Empty((await store.ListConversationsAsync(Guid.NewGuid(), 10, null, null, null)).Items);
    }

    /// <summary>
    /// Verifies that conversation paging walks every chat once without repeats.
    /// </summary>
    [Fact]
    public async Task ConversationPaging_WalksEveryChatOnce_WithoutRepeats()
    {
        var store = CreateStore();
        var owner = Guid.NewGuid();
        var expected = new List<string>();
        for (var i = 0; i < 7; i++)
        {
            expected.Add(await store.CreateConversationAsync(owner, "chat " + i));
            await Task.Delay(5);
        }
        expected.Reverse();
        await store.UpdateConversationAsync(owner, expected[6], null, true);
        var order = new List<string> { expected[6] };
        order.AddRange(expected.Take(6));

        var seen = new List<string>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var page = await store.ListConversationsAsync(owner, 3, cursor, null, null);
            seen.AddRange(page.Items.Select(x => x.Id));
            cursor = page.NextCursor;
            pages++;
        }
        while (cursor is not null && pages < 10);

        Assert.Equal(3, pages);
        Assert.Equal(order, seen);
    }

    /// <summary>
    /// Verifies that conversation search matches the title without case or wildcard surprises.
    /// </summary>
    [Fact]
    public async Task ConversationSearch_MatchesTheTitle_WithoutCaseOrWildcardSurprises()
    {
        var store = CreateStore();
        var owner = Guid.NewGuid();
        var london = await store.CreateConversationAsync(owner, "Londra'daki Firmalar");
        await store.CreateConversationAsync(owner, "Brightwave fırsatları");
        var percent = await store.CreateConversationAsync(owner, "100% hedef");

        Assert.Equal(new[] { london }, (await store.ListConversationsAsync(owner, 10, null, "londra", null)).Items.Select(x => x.Id).ToArray());
        Assert.Equal(new[] { percent }, (await store.ListConversationsAsync(owner, 10, null, "%", null)).Items.Select(x => x.Id).ToArray());
        Assert.Empty((await store.ListConversationsAsync(owner, 10, null, "_", null)).Items);
        Assert.Equal(3, (await store.ListConversationsAsync(owner, 10, null, "  ", null)).Items.Count);
    }

    /// <summary>
    /// Verifies that aBroken cursor just starts from the top.
    /// </summary>
    [Fact]
    public async Task ABrokenCursor_JustStartsFromTheTop()
    {
        var store = CreateStore();
        var owner = Guid.NewGuid();
        await store.CreateConversationAsync(owner, "x");

        var page = await store.ListConversationsAsync(owner, 10, "garbage!!", null, null);

        Assert.Single(page.Items);
    }

    /// <summary>
    /// Verifies that updating aConversation changes only what was given and only for the owner.
    /// </summary>
    [Fact]
    public async Task UpdatingAConversation_ChangesOnlyWhatWasGiven_AndOnlyForTheOwner()
    {
        var store = CreateStore();
        var owner = Guid.NewGuid();
        var id = await store.CreateConversationAsync(owner, "old");

        Assert.True(await store.UpdateConversationAsync(owner, id, "new name", null));
        Assert.True(await store.UpdateConversationAsync(owner, id, null, true));
        var chat = (await store.GetConversationAsync(owner, id))!;
        Assert.Equal("new name", chat.Title);
        Assert.True(chat.Pinned);

        Assert.False(await store.UpdateConversationAsync(Guid.NewGuid(), id, "hijack", false));
        Assert.False(await store.UpdateConversationAsync(owner, Guid.NewGuid().ToString("N"), "x", null));
        Assert.False(await store.UpdateConversationAsync(owner, "bad", "x", null));
        var after = (await store.GetConversationAsync(owner, id))!;
        Assert.Equal("new name", after.Title);
        Assert.True(after.Pinned);
    }

    /// <summary>
    /// Verifies that deleting aConversation removes its entries and nobody elses.
    /// </summary>
    [Fact]
    public async Task DeletingAConversation_RemovesItsEntries_AndNobodyElses()
    {
        var store = CreateStore();
        var owner = Guid.NewGuid();
        var chat = await store.CreateConversationAsync(owner, "gone");
        var keepChat = await store.CreateConversationAsync(owner, "kept");
        var gone = await store.AddAsync(owner, "q1", "product", Fetch, null, chat);
        var kept = await store.AddAsync(owner, "q2", "product", Fetch, null, keepChat);

        Assert.False(await store.DeleteConversationAsync(Guid.NewGuid(), chat));
        Assert.NotNull(await store.GetAsync(owner, gone));

        Assert.True(await store.DeleteConversationAsync(owner, chat));
        Assert.Null(await store.GetConversationAsync(owner, chat));
        Assert.Null(await store.GetAsync(owner, gone));
        Assert.NotNull(await store.GetAsync(owner, kept));
        Assert.False(await store.DeleteConversationAsync(owner, chat));
    }

    /// <summary>
    /// Verifies that delete all keeps pinned chats by default and touches no one else.
    /// </summary>
    [Fact]
    public async Task DeleteAll_KeepsPinnedChatsByDefault_AndTouchesNoOneElse()
    {
        var store = CreateStore();
        var owner = Guid.NewGuid();
        var other = Guid.NewGuid();
        var plain = await store.CreateConversationAsync(owner, "plain");
        var pinned = await store.CreateConversationAsync(owner, "pinned");
        await store.UpdateConversationAsync(owner, pinned, null, true);
        var plainEntry = await store.AddAsync(owner, "q", "product", Fetch, null, plain);
        var pinnedEntry = await store.AddAsync(owner, "q", "product", Fetch, null, pinned);
        var legacy = await store.AddAsync(owner, "legacy, no chat", "product", Fetch);
        var otherChat = await store.CreateConversationAsync(other, "theirs");
        var otherEntry = await store.AddAsync(other, "q", "product", Fetch, null, otherChat);

        var removed = await store.DeleteAllConversationsAsync(owner, false);

        Assert.Equal(1, removed);
        Assert.Null(await store.GetConversationAsync(owner, plain));
        Assert.Null(await store.GetAsync(owner, plainEntry));
        Assert.Null(await store.GetAsync(owner, legacy));
        Assert.NotNull(await store.GetConversationAsync(owner, pinned));
        Assert.NotNull(await store.GetAsync(owner, pinnedEntry));
        Assert.NotNull(await store.GetConversationAsync(other, otherChat));
        Assert.NotNull(await store.GetAsync(other, otherEntry));

        Assert.Equal(1, await store.DeleteAllConversationsAsync(owner, true));
        Assert.Null(await store.GetConversationAsync(owner, pinned));
        Assert.Null(await store.GetAsync(owner, pinnedEntry));
    }

    /// <summary>
    /// Verifies that conversation counts separate pinned from total.
    /// </summary>
    [Fact]
    public async Task ConversationCounts_SeparatePinnedFromTotal()
    {
        var store = CreateStore();
        var owner = Guid.NewGuid();
        await store.CreateConversationAsync(owner, "a");
        var b = await store.CreateConversationAsync(owner, "b");
        await store.CreateConversationAsync(Guid.NewGuid(), "someone else");
        await store.UpdateConversationAsync(owner, b, null, true);

        var counts = await store.CountConversationsAsync(owner);

        Assert.Equal(2, counts.Total);
        Assert.Equal(1, counts.Pinned);
        Assert.Equal(new ConversationCounts(0, 0), await store.CountConversationsAsync(Guid.NewGuid()));
    }

    /// <summary>
    /// Verifies that messages come newest page first oldest to newest inside the page and page backwards.
    /// </summary>
    [Fact]
    public async Task Messages_ComeNewestPageFirst_OldestToNewestInsideThePage_AndPageBackwards()
    {
        var store = CreateStore();
        var owner = Guid.NewGuid();
        var chat = await store.CreateConversationAsync(owner, "long");
        var ids = new List<string>();
        for (var i = 0; i < 5; i++)
        {
            ids.Add(await store.AddAsync(owner, "q" + i, "product", Fetch, null, chat));
            await Task.Delay(5);
        }

        var last = await store.ListMessagesAsync(owner, chat, 2, null);
        Assert.Equal(new[] { ids[3], ids[4] }, last.Items.Select(e => e.Id).ToArray());
        Assert.NotNull(last.NextCursor);

        var middle = await store.ListMessagesAsync(owner, chat, 2, last.NextCursor);
        Assert.Equal(new[] { ids[1], ids[2] }, middle.Items.Select(e => e.Id).ToArray());

        var first = await store.ListMessagesAsync(owner, chat, 2, middle.NextCursor);
        Assert.Equal(new[] { ids[0] }, first.Items.Select(e => e.Id).ToArray());
        Assert.Null(first.NextCursor);
    }

    /// <summary>
    /// Verifies that messages and last entry of someone elses conversation are empty.
    /// </summary>
    [Fact]
    public async Task Messages_AndLastEntry_OfSomeoneElsesConversation_AreEmpty()
    {
        var store = CreateStore();
        var owner = Guid.NewGuid();
        var chat = await store.CreateConversationAsync(owner, "mine");
        var entry = await store.AddAsync(owner, "q", "product", Fetch, null, chat);

        Assert.Equal(entry, (await store.LastEntryAsync(owner, chat))!.Id);
        Assert.Null(await store.LastEntryAsync(Guid.NewGuid(), chat));
        Assert.Empty((await store.ListMessagesAsync(Guid.NewGuid(), chat, 10, null)).Items);
        Assert.Null(await store.LastEntryAsync(owner, "bad"));
        Assert.Null(await store.LastEntryAsync(owner, await store.CreateConversationAsync(owner, "empty")));
    }

    /// <summary>
    /// Verifies that an entry added without the flag is never listed for admin view.
    /// </summary>
    [Fact]
    public async Task AnEntryAddedWithoutTheFlag_IsNeverListedForAdminView()
    {
        var store = CreateStore();
        var owner = Guid.NewGuid();
        await store.AddAsync(owner, "private question", "product", Fetch);

        Assert.Empty(await store.ListViewableQuestionsAsync(owner, 50));
    }

    /// <summary>
    /// Verifies that flagged entries are listed newest first for their owner only with text time and table.
    /// </summary>
    [Fact]
    public async Task FlaggedEntries_AreListedNewestFirst_ForTheirOwnerOnly_WithTextTimeAndTable()
    {
        var store = CreateStore();
        var owner = Guid.NewGuid();
        await store.AddAsync(owner, "first visible", "account", Fetch, null, null, default, adminViewable: true);
        await store.AddAsync(owner, "hidden one", "contract", Fetch);
        await store.AddAsync(owner, "second visible", "contract", Fetch, null, null, default, adminViewable: true);

        var list = await store.ListViewableQuestionsAsync(owner, 50);

        Assert.Equal(new[] { "second visible", "first visible" }, list.Select(q => q.Question).ToArray());
        Assert.Equal(new[] { "contract", "account" }, list.Select(q => q.Entity).ToArray());
        Assert.True(Math.Abs((DateTimeOffset.UtcNow - list[0].At).TotalMinutes) < 5);
        Assert.Empty(await store.ListViewableQuestionsAsync(Guid.NewGuid(), 50));
    }

    /// <summary>
    /// Verifies that aDeleted flagged entry disappears from the admin view no separate copy is kept.
    /// </summary>
    [Fact]
    public async Task ADeletedFlaggedEntry_DisappearsFromTheAdminView_NoSeparateCopyIsKept()
    {
        var store = CreateStore();
        var owner = Guid.NewGuid();
        var id = await store.AddAsync(owner, "will be deleted", "account", Fetch, null, null, default, adminViewable: true);
        Assert.Single(await store.ListViewableQuestionsAsync(owner, 50));

        await store.DeleteAsync(owner, id);

        Assert.Empty(await store.ListViewableQuestionsAsync(owner, 50));
    }

    /// <summary>
    /// Verifies that the admin view list honours take.
    /// </summary>
    [Fact]
    public async Task TheAdminViewListHonoursTake()
    {
        var store = CreateStore();
        var owner = Guid.NewGuid();
        for (var i = 0; i < 5; i++)
        {
            await store.AddAsync(owner, "q" + i, "account", Fetch, null, null, default, adminViewable: true);
        }

        Assert.Equal(2, (await store.ListViewableQuestionsAsync(owner, 2)).Count);
    }
}
