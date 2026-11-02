using CrmNlQuery.Tests.Support;
using CrmNlQuery.Application.Ask.Models;
using CrmNlQuery.Application.Drafts.Models;
using CrmNlQuery.Application.Drafts.Services;
using CrmNlQuery.Application.History.Services;
using CrmNlQuery.Application.Summary.Services;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Infrastructure.Persistence.Memory.History;
using static CrmNlQuery.Tests.Application.Drafts.Services.DraftServiceTests;

namespace CrmNlQuery.Tests.Application.Drafts;

/// <summary>
/// Tests that a draft is a history record storing only the placeholder template and refilled without the model when opened.
/// </summary>
public class DraftHistoryTests
{
    private static readonly Caller Alice = Caller.User(Guid.NewGuid());

    private static (ResultService Results, MemoryHistoryStore History, Executor Executor, FakeLlm Llm) Build(Func<bool>? enabled = null)
    {
        var metadata = new FakeMetadataSource(Everything());
        var executor = new Executor();
        var summary = new SummaryBuilder(metadata, executor, new Views(), () => TimeSpan.FromSeconds(5), () => new DateTimeOffset(2026, 10, 23, 9, 0, 0, TimeSpan.Zero));
        var llm = new FakeLlm();
        var service = new DraftService(new DraftValueReader(metadata, executor, summary, () => new DateTimeOffset(2026, 10, 23, 9, 0, 0, TimeSpan.Zero)), new DraftComposer(llm));
        var history = new MemoryHistoryStore();
        return (new ResultService(history, metadata, executor, null, summary, service, enabled), history, executor, llm);
    }

    private static DraftOutcome Draft() => new DraftOutcome("account", AccountId.ToString("N"), "Brightwave Appliances Ltd", "Following up: Brightwave Appliances Ltd",
        "Dear Ayse, ...", "ayse.demir@brightwave.example", DraftIntent.Formal, new List<string>(),
        "Following up: {{customer_name}}", "Dear {{contact_first_name}}, you have {{open_opportunity_count}} open opportunities.\n{{sender_name}}");

    /// <summary>
    /// Verifies that publishing stores only the template, never a customer value.
    /// </summary>
    [Fact]
    public async Task PublishStoresOnlyTheTemplate_NeverACustomerValue()
    {
        var (results, history, _, _) = Build();
        var owner = Guid.NewGuid();

        var published = await results.PublishDraftAsync(Draft(), owner, "write a follow-up email to this customer");

        var entry = (await history.GetAsync(owner, published.ResultId!))!;
        Assert.Equal("draft", entry.Kind);
        Assert.Equal("account", entry.Entity);
        Assert.Equal(AccountId, SummaryRecord.IdOf(entry.FetchXml));
        Assert.Contains("{{customer_name}}", entry.Payload);
        foreach (var value in new[] { "Brightwave", "Ayse", "ayse.demir" })
        {
            Assert.DoesNotContain(value, entry.Payload!);
        }
    }

    /// <summary>
    /// Verifies that reopening refills fresh values as the caller without the model.
    /// </summary>
    [Fact]
    public async Task Reopening_RefillsFreshValuesAsTheCaller_WithoutTheModel()
    {
        var (results, _, executor, llm) = Build();
        var owner = Alice.UserId;
        var published = await results.PublishDraftAsync(Draft(), owner, "write a follow-up email");

        var reopened = await results.OpenAnyAsync(published.ResultId!, owner);

        var draft = Assert.IsType<DraftOutcome>(reopened);
        Assert.Equal(published.ResultId, draft.ResultId);
        Assert.Equal("Following up: " + "name-value", draft.Subject);
        Assert.Contains("Dear Ayse,", draft.Body);
        Assert.Contains("3 open opportunities", draft.Body);
        Assert.Equal("ayse.demir@brightwave.example", draft.To);
        Assert.Empty(llm.Calls);
        Assert.NotEmpty(executor.Callers);
        Assert.All(executor.Callers, c => Assert.Equal(Alice, c));
    }

    /// <summary>
    /// Verifies that someone else's draft, a disabled feature and a corrupt payload are all unavailable.
    /// </summary>
    [Fact]
    public async Task ADraftOfSomeoneElse_AFeatureThatIsOff_AndACorruptPayload_AreAllNotAvailable()
    {
        var (results, history, _, _) = Build();
        var owner = Guid.NewGuid();
        var published = await results.PublishDraftAsync(Draft(), owner, "q");
        Assert.Null(await results.OpenAnyAsync(published.ResultId!, Guid.NewGuid()));

        var (off, offHistory, _, _) = Build(() => false);
        var offPublished = await off.PublishDraftAsync(Draft(), owner, "q");
        Assert.Null(await off.OpenAnyAsync(offPublished.ResultId!, owner));

        var corrupt = await history.AddAsync(owner, "q", "account", "<fetch top=\"1\"><entity name=\"account\"><filter><condition attribute=\"accountid\" operator=\"eq\" value=\"" + AccountId + "\" /></filter></entity></fetch>", kind: "draft", payload: "not json");
        Assert.Null(await results.OpenAnyAsync(corrupt, owner));
    }

    /// <summary>
    /// Verifies that a draft is guarded like a summary and never becomes the context of a follow up.
    /// </summary>
    [Fact]
    public async Task ADraft_IsGuardedLikeASummary_AndNeverBecomesTheContextOfAFollowUp()
    {
        var (results, history, _, _) = Build();
        var owner = Guid.NewGuid();
        var chat = await history.CreateConversationAsync(owner, "chat");
        var published = await results.PublishDraftAsync(Draft(), owner, "write an email", chat);

        Assert.True(await results.IsSummaryAsync(published.ResultId!, owner));
        var context = new ConversationContext(history, () => TimeSpan.FromMinutes(30));
        Assert.Null(await context.LoadLastAsync(owner, chat));
        Assert.Null(await context.LoadAsync(owner, published.ResultId!, ignoreAge: true));
    }

    /// <summary>
    /// Verifies that the payload round trips and broken ones are rejected.
    /// </summary>
    [Fact]
    public void ThePayload_RoundTrips_AndBrokenOnesAreRejected()
    {
        var json = new DraftPayload("S {{customer_name}}", "B", "warm").ToJson();

        Assert.Equal(new DraftPayload("S {{customer_name}}", "B", "warm"), DraftPayload.Parse(json));
        Assert.Null(DraftPayload.Parse(null));
        Assert.Null(DraftPayload.Parse("{}"));
        Assert.Null(DraftPayload.Parse("[1]"));
    }
}
