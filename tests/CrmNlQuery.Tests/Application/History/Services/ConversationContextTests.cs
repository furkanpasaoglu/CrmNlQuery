using CrmNlQuery.Application.History.Services;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Infrastructure.Persistence.Memory.History;

namespace CrmNlQuery.Tests.Application.History.Services;

/// <summary>
/// Tests loading the previous answer on the server for follow-up questions: ownership, time window and broken ids.
/// </summary>
public class ConversationContextTests
{
    private const string Fetch = "<fetch top=\"500\"><entity name=\"account\"><attribute name=\"name\" /></entity></fetch>";

    private sealed class Clock
    {
        public DateTimeOffset Now { get; set; } = new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);
    }

    private readonly Clock _clock = new Clock();
    private readonly MemoryHistoryStore _history;
    private TimeSpan _maxAge = TimeSpan.FromMinutes(30);

    public ConversationContextTests()
    {
        _history = new MemoryHistoryStore(() => _clock.Now);
    }

    private ConversationContext Context() => new ConversationContext(_history, () => _maxAge, () => _clock.Now);

    /// <summary>
    /// Verifies that it loads the stored query not rows and the entry ID.
    /// </summary>
    [Fact]
    public async Task ItLoadsTheStoredQuery_NotRows_AndTheEntryId()
    {
        var owner = Guid.NewGuid();
        var id = await _history.AddAsync(owner, "Hamburg'daki hesaplar", "account", Fetch);

        var loaded = await Context().LoadAsync(owner, id);

        Assert.Equal(new PreviousQuery("Hamburg'daki hesaplar", "account", Fetch), loaded!.Query);
        Assert.Equal(id, loaded.ResultId);
    }

    /// <summary>
    /// Verifies that another users entry an unknown ID a broken ID and nothing all give null.
    /// </summary>
    [Fact]
    public async Task AnotherUsersEntry_AnUnknownId_ABrokenId_AndNothing_AllGiveNull()
    {
        var id = await _history.AddAsync(Guid.NewGuid(), "q", "account", Fetch);
        var someoneElse = Guid.NewGuid();
        var context = Context();

        Assert.Null(await context.LoadAsync(someoneElse, id));
        Assert.Null(await context.LoadAsync(someoneElse, "00000000000000000000000000000000"));
        Assert.Null(await context.LoadAsync(someoneElse, "not an id"));
        Assert.Null(await context.LoadAsync(someoneElse, ""));
        Assert.Null(await context.LoadAsync(someoneElse, null));
        Assert.Null(await context.LoadAsync(someoneElse, "   "));
    }

    /// <summary>
    /// Verifies that an answer older than the window is not used but one inside is and the edge is inclusive.
    /// </summary>
    [Fact]
    public async Task AnAnswerOlderThanTheWindow_IsNotUsed_ButOneInsideIsAndTheEdgeIsInclusive()
    {
        var owner = Guid.NewGuid();
        var id = await _history.AddAsync(owner, "q", "account", Fetch);
        var context = Context();

        _clock.Now = _clock.Now.AddMinutes(30);
        Assert.NotNull(await context.LoadAsync(owner, id));

        _clock.Now = _clock.Now.AddSeconds(1);
        Assert.Null(await context.LoadAsync(owner, id));
    }

    /// <summary>
    /// Verifies that ignore age skips only the window not the ownership check.
    /// </summary>
    [Fact]
    public async Task IgnoreAge_SkipsOnlyTheWindow_NotTheOwnershipCheck()
    {
        var owner = Guid.NewGuid();
        var id = await _history.AddAsync(owner, "q", "account", Fetch);
        _clock.Now = _clock.Now.AddDays(5);
        var context = Context();

        Assert.NotNull(await context.LoadAsync(owner, id, ignoreAge: true));
        Assert.Null(await context.LoadAsync(Guid.NewGuid(), id, ignoreAge: true));
    }

    /// <summary>
    /// Verifies that the window is read on every call so a panel change applies immediately.
    /// </summary>
    [Fact]
    public async Task TheWindowIsReadOnEveryCall_SoAPanelChangeAppliesImmediately()
    {
        var owner = Guid.NewGuid();
        var id = await _history.AddAsync(owner, "q", "account", Fetch);
        _clock.Now = _clock.Now.AddMinutes(20);
        var context = Context();
        Assert.NotNull(await context.LoadAsync(owner, id));

        _maxAge = TimeSpan.FromMinutes(10);

        Assert.Null(await context.LoadAsync(owner, id));
    }

    /// <summary>
    /// Verifies that a whitespace around the ID is tolerated.
    /// </summary>
    [Fact]
    public async Task AWhitespaceAroundTheId_IsTolerated()
    {
        var owner = Guid.NewGuid();
        var id = await _history.AddAsync(owner, "q", "account", Fetch);

        Assert.NotNull(await Context().LoadAsync(owner, "  " + id + " "));
    }

    /// <summary>
    /// Verifies that load last returns the newest entry of that chat not of another chat.
    /// </summary>
    [Fact]
    public async Task LoadLast_ReturnsTheNewestEntryOfThatChat_NotOfAnotherChat()
    {
        var owner = Guid.NewGuid();
        var chatA = await _history.CreateConversationAsync(owner, "A");
        var chatB = await _history.CreateConversationAsync(owner, "B");
        await _history.AddAsync(owner, "a1", "account", Fetch, null, chatA);
        _clock.Now = _clock.Now.AddMinutes(1);
        var a2 = await _history.AddAsync(owner, "a2", "account", Fetch, null, chatA);
        _clock.Now = _clock.Now.AddMinutes(1);
        await _history.AddAsync(owner, "b1", "account", Fetch, null, chatB);

        var loaded = await Context().LoadLastAsync(owner, chatA);

        Assert.Equal("a2", loaded!.Query.Question);
        Assert.Equal(a2, loaded.ResultId);
        Assert.Equal(chatA, loaded.ConversationId);
    }

    /// <summary>
    /// Verifies that load last is null for someone elses chat broken IDs empty chats and too old ones.
    /// </summary>
    [Fact]
    public async Task LoadLast_IsNullForSomeoneElsesChat_BrokenIds_EmptyChats_AndTooOldOnes()
    {
        var owner = Guid.NewGuid();
        var chat = await _history.CreateConversationAsync(owner, "A");
        var empty = await _history.CreateConversationAsync(owner, "empty");
        await _history.AddAsync(owner, "q", "account", Fetch, null, chat);

        Assert.Null(await Context().LoadLastAsync(Guid.NewGuid(), chat));
        Assert.Null(await Context().LoadLastAsync(owner, "bozuk"));
        Assert.Null(await Context().LoadLastAsync(owner, null));
        Assert.Null(await Context().LoadLastAsync(owner, empty));
        Assert.NotNull(await Context().LoadLastAsync(owner, chat));
        _clock.Now = _clock.Now.AddMinutes(31);
        Assert.Null(await Context().LoadLastAsync(owner, chat));
    }
}
