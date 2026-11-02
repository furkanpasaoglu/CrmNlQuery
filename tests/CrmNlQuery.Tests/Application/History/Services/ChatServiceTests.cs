using CrmNlQuery.Application.History.Abstractions;
using CrmNlQuery.Application.History.Services;
using CrmNlQuery.Infrastructure.Persistence.Memory.History;

namespace CrmNlQuery.Tests.Application.History.Services;

/// <summary>
/// Unit tests for chat rules: ownership, title cleaning, pinning, deletion, delete-all, page limits and automatic titles.
/// </summary>
public class ChatServiceTests
{
    private const string Fetch = "<fetch top=\"500\"><entity name=\"product\" /></fetch>";

    private static (ChatService Service, IHistoryStore Store) Create()
    {
        var store = new MemoryHistoryStore();
        return (new ChatService(store), store);
    }

    /// <summary>
    /// Verifies that auto title is the first question on one line.
    /// </summary>
    [Theory]
    [InlineData("Londra'daki firmaları getir", "Londra'daki firmaları getir")]
    [InlineData("  çok   boşluklu \n soru  ", "çok boşluklu soru")]
    [InlineData("", "New chat")]
    [InlineData(null, "New chat")]
    public void AutoTitle_IsTheFirstQuestionOnOneLine(string? question, string expected)
    {
        Assert.Equal(expected, ChatService.AutoTitle(question));
    }

    /// <summary>
    /// Verifies that auto title is capped at40 characters.
    /// </summary>
    [Fact]
    public void AutoTitle_IsCappedAt40Characters()
    {
        var title = ChatService.AutoTitle(new string('a', 200));
        Assert.True(title.Length <= ChatService.AutoTitleLength);
        Assert.EndsWith("…", title);
    }

    /// <summary>
    /// Verifies that the title loses control and hidden characters and is capped at60.
    /// </summary>
    [Fact]
    public void TheTitle_LosesControlAndHiddenCharacters_AndIsCappedAt60()
    {
        Assert.Equal("a b c", ChatService.CleanTitle("a\r\nb​c".Replace("b​", "b ")));
        Assert.DoesNotContain('‮', ChatService.CleanTitle("x‮y"));
        Assert.True(ChatService.CleanTitle(new string('z', 500)).Length <= ChatService.MaxTitleLength);
        Assert.Equal("", ChatService.CleanTitle("   \n\t "));
    }

    /// <summary>
    /// Verifies that rename cleans the name rejects blank ones and only works for the owner.
    /// </summary>
    [Fact]
    public async Task Rename_CleansTheName_RejectsBlankOnes_AndOnlyWorksForTheOwner()
    {
        var (service, store) = Create();
        var me = Guid.NewGuid();
        var id = await service.EnsureAsync(me, null, "ilk soru");

        Assert.True(await service.RenameAsync(me, id, "  Yeni\nad  "));
        Assert.Equal("Yeni ad", (await store.GetConversationAsync(me, id))!.Title);

        Assert.False(await service.RenameAsync(me, id, "   "));
        Assert.False(await service.RenameAsync(me, id, null));
        Assert.False(await service.RenameAsync(Guid.NewGuid(), id, "hijack"));
        Assert.Equal("Yeni ad", (await store.GetConversationAsync(me, id))!.Title);
    }

    /// <summary>
    /// Verifies that pin and delete work only for the owner.
    /// </summary>
    [Fact]
    public async Task PinAndDelete_WorkOnlyForTheOwner()
    {
        var (service, store) = Create();
        var me = Guid.NewGuid();
        var stranger = Guid.NewGuid();
        var id = await service.EnsureAsync(me, null, "q");

        Assert.False(await service.PinAsync(stranger, id, true));
        Assert.False((await store.GetConversationAsync(me, id))!.Pinned);
        Assert.True(await service.PinAsync(me, id, true));
        Assert.True((await store.GetConversationAsync(me, id))!.Pinned);

        Assert.False(await service.DeleteAsync(stranger, id));
        Assert.NotNull(await store.GetConversationAsync(me, id));
        Assert.True(await service.DeleteAsync(me, id));
        Assert.Null(await store.GetConversationAsync(me, id));
    }

    /// <summary>
    /// Verifies that delete all keeps pinned chats unless asked and counts are separate.
    /// </summary>
    [Fact]
    public async Task DeleteAll_KeepsPinnedChatsUnlessAsked_AndCountsAreSeparate()
    {
        var (service, store) = Create();
        var me = Guid.NewGuid();
        var a = await service.EnsureAsync(me, null, "a");
        var b = await service.EnsureAsync(me, null, "b");
        await service.EnsureAsync(me, null, "c");
        await service.PinAsync(me, b, true);

        var counts = await service.CountsAsync(me);
        Assert.Equal(3, counts.Total);
        Assert.Equal(1, counts.Pinned);

        Assert.Equal(2, await service.DeleteAllAsync(me, false));
        Assert.Null(await store.GetConversationAsync(me, a));
        Assert.NotNull(await store.GetConversationAsync(me, b));

        Assert.Equal(1, await service.DeleteAllAsync(me, true));
        Assert.Equal(0, (await service.CountsAsync(me)).Total);
    }

    /// <summary>
    /// Verifies that ensure reuses the callers chat but opens a new one for someone elses or broken IDs.
    /// </summary>
    [Fact]
    public async Task Ensure_ReusesTheCallersChat_ButOpensANewOneForSomeoneElsesOrBrokenIds()
    {
        var (service, store) = Create();
        var me = Guid.NewGuid();
        var stranger = Guid.NewGuid();
        var mine = await service.EnsureAsync(me, null, "ilk");

        Assert.Equal(mine, await service.EnsureAsync(me, mine, "ikinci"));
        var viaStranger = await service.EnsureAsync(stranger, mine, "başkası");
        Assert.NotEqual(mine, viaStranger);
        Assert.Equal("başkası", (await store.GetConversationAsync(stranger, viaStranger))!.Title);
        Assert.NotEqual(mine, await service.EnsureAsync(me, "bozuk", "x"));
        Assert.False(await service.OwnsAsync(stranger, mine));
        Assert.True(await service.OwnsAsync(me, mine));
        Assert.False(await service.OwnsAsync(me, null));
    }

    /// <summary>
    /// Verifies that list shows only the callers chats clamps the page size and searches.
    /// </summary>
    [Fact]
    public async Task List_ShowsOnlyTheCallersChats_ClampsThePageSize_AndSearches()
    {
        var (service, _) = Create();
        var me = Guid.NewGuid();
        for (var i = 0; i < 120; i++)
        {
            await service.EnsureAsync(me, null, "soru " + i);
        }
        await service.EnsureAsync(Guid.NewGuid(), null, "başkasının");

        Assert.Equal(ChatService.DefaultListTake, (await service.ListAsync(me, null, null, null, null)).Items.Count);
        Assert.Single((await service.ListAsync(me, 0, null, null, null)).Items);
        Assert.Equal(ChatService.MaxListTake, (await service.ListAsync(me, 100000, null, null, null)).Items.Count);
        var found = await service.ListAsync(me, null, null, "SORU 77", null);
        Assert.Equal("soru 77", Assert.Single(found.Items).Title);
        Assert.Empty((await service.ListAsync(me, null, null, "başkasının", null)).Items);
    }

    /// <summary>
    /// Verifies that messages are null for someone elses chat and paged otherwise.
    /// </summary>
    [Fact]
    public async Task Messages_AreNullForSomeoneElsesChat_AndPagedOtherwise()
    {
        var (service, store) = Create();
        var me = Guid.NewGuid();
        var chat = await service.EnsureAsync(me, null, "ilk");
        for (var i = 0; i < 25; i++)
        {
            await store.AddAsync(me, "q" + i, "product", Fetch, null, chat);
        }

        Assert.Null(await service.MessagesAsync(Guid.NewGuid(), chat, null, null));
        Assert.Null(await service.MessagesAsync(me, "bozuk", null, null));
        var first = await service.MessagesAsync(me, chat, null, null);
        Assert.Equal(ChatService.DefaultMessageTake, first!.Items.Count);
        Assert.NotNull(first.NextCursor);
        var second = await service.MessagesAsync(me, chat, null, first.NextCursor);
        Assert.Equal(5, second!.Items.Count);
        Assert.Null(second.NextCursor);
        Assert.Equal(25, (await service.MessagesAsync(me, chat, 100000, null))!.Items.Count);
    }
}
