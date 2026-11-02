using CrmNlQuery.Application.Quality.Abstractions;
using CrmNlQuery.Application.Quality.Models;
using CrmNlQuery.Domain.Quality.Entities;

namespace CrmNlQuery.Tests.Contracts.Quality;

/// <summary>
/// Shared behaviours every acceptance-set store implementation (memory, PostgreSQL) must pass without polluting real data.
/// </summary>
public abstract class QualityStoreContract
{
    protected abstract IQualityStore CreateStore();

    private static QualityCase Make(string tag, string entity = "incident", bool verified = false, string status = QualityCase.StatusActive, string source = QualityCase.SourceGenerated)
        => new QualityCase
        {
            CreatedBy = @"LAB\boss",
            CreatedAt = DateTimeOffset.UtcNow,
            Source = source,
            Status = status,
            Verified = verified,
            Language = "tr",
            Question = "contract-test " + tag,
            Entity = entity,
            AnswersJson = "{\"columns\":\"custom:title\"}",
            ReferenceFetch = "<fetch><entity name='" + entity + "'><attribute name='title'/></entity></fetch>",
            CompareJson = "{\"columns\":\"last\",\"ties\":\"strict\"}",
            Template = "list",
            Note = "n " + tag,
        };

    private static async Task Cleanup(IQualityStore store, params QualityCase[] items) => await store.DeleteAsync(items.Select(i => i.Id).ToList());

    /// <summary>
    /// Verifies that an added case is read back with every field.
    /// </summary>
    [Fact]
    public async Task AnAddedCase_IsReadBackWithEveryField()
    {
        var store = CreateStore();
        var item = Make(Guid.NewGuid().ToString("N"));
        try
        {
            await store.AddAsync(item);

            var read = await store.GetAsync(item.Id);

            Assert.NotNull(read);
            Assert.Equal(item.Question, read!.Question);
            Assert.Equal(item.Entity, read.Entity);
            Assert.Equal(item.AnswersJson, read.AnswersJson);
            Assert.Equal(item.ReferenceFetch, read.ReferenceFetch);
            Assert.Equal(item.CompareJson, read.CompareJson);
            Assert.Equal("tr", read.Language);
            Assert.Equal("list", read.Template);
            Assert.Equal(@"LAB\boss", read.CreatedBy);
            Assert.False(read.Verified);
        }
        finally { await Cleanup(store, item); }
    }

    /// <summary>
    /// Verifies that the filter narrows by status, source, verified flag and entity.
    /// </summary>
    [Fact]
    public async Task TheFilter_NarrowsByStatusSourceVerifiedAndEntity()
    {
        var store = CreateStore();
        var tag = Guid.NewGuid().ToString("N");
        var a = Make(tag + "a", "incident", verified: true);
        var b = Make(tag + "b", "account", status: QualityCase.StatusRetired);
        var c = Make(tag + "c", "account", source: QualityCase.SourceMarked, verified: true);
        try
        {
            foreach (var item in new[] { a, b, c }) { await store.AddAsync(item); }

            var ours = (await store.ListAsync(new QualityCaseFilter())).Where(x => x.Question.StartsWith("contract-test " + tag)).ToList();
            Assert.Equal(3, ours.Count);
            Assert.Equal(new[] { a.Id }, (await store.ListAsync(new QualityCaseFilter(Entity: "incident", Verified: true))).Where(x => x.Question.Contains(tag)).Select(x => x.Id));
            Assert.Equal(new[] { b.Id }, (await store.ListAsync(new QualityCaseFilter(Status: QualityCase.StatusRetired))).Where(x => x.Question.Contains(tag)).Select(x => x.Id));
            Assert.Equal(new[] { c.Id }, (await store.ListAsync(new QualityCaseFilter(Source: QualityCase.SourceMarked))).Where(x => x.Question.Contains(tag)).Select(x => x.Id));
        }
        finally { await Cleanup(store, a, b, c); }
    }

    /// <summary>
    /// Verifies that exists matches the same question and query.
    /// </summary>
    [Fact]
    public async Task Exists_MatchesTheSameQuestionAndQuery()
    {
        var store = CreateStore();
        var item = Make(Guid.NewGuid().ToString("N"));
        try
        {
            await store.AddAsync(item);

            Assert.True(await store.ExistsAsync(item.Question, item.ReferenceFetch));
            Assert.False(await store.ExistsAsync(item.Question, item.ReferenceFetch + " "));
            Assert.False(await store.ExistsAsync("another " + item.Question, item.ReferenceFetch));
        }
        finally { await Cleanup(store, item); }
    }

    /// <summary>
    /// Verifies that status and verification changes affect only the chosen cases.
    /// </summary>
    [Fact]
    public async Task StatusAndVerification_ChangeOnlyTheChosenCases()
    {
        var store = CreateStore();
        var tag = Guid.NewGuid().ToString("N");
        var a = Make(tag + "a");
        var b = Make(tag + "b");
        try
        {
            await store.AddAsync(a);
            await store.AddAsync(b);

            Assert.Equal(1, await store.SetVerifiedAsync(new[] { a.Id }, true));
            Assert.Equal(1, await store.SetStatusAsync(new[] { b.Id }, QualityCase.StatusRetired));

            Assert.True((await store.GetAsync(a.Id))!.Verified);
            Assert.False((await store.GetAsync(b.Id))!.Verified);
            Assert.Equal(QualityCase.StatusRetired, (await store.GetAsync(b.Id))!.Status);
            Assert.Equal(QualityCase.StatusActive, (await store.GetAsync(a.Id))!.Status);
        }
        finally { await Cleanup(store, a, b); }
    }

    /// <summary>
    /// Verifies that Delete removes only the chosen cases and ignores unknown ids.
    /// </summary>
    [Fact]
    public async Task Delete_RemovesOnlyTheChosenCases_AndUnknownIdsAreIgnored()
    {
        var store = CreateStore();
        var tag = Guid.NewGuid().ToString("N");
        var a = Make(tag + "a");
        var b = Make(tag + "b");
        try
        {
            await store.AddAsync(a);
            await store.AddAsync(b);

            Assert.Equal(1, await store.DeleteAsync(new[] { a.Id, "not-an-id", Guid.NewGuid().ToString("N") }));

            Assert.Null(await store.GetAsync(a.Id));
            Assert.NotNull(await store.GetAsync(b.Id));
            Assert.Equal(0, await store.DeleteAsync(new[] { "zzz" }));
        }
        finally { await Cleanup(store, a, b); }
    }

    /// <summary>
    /// Verifies that the count follows additions and deletions.
    /// </summary>
    [Fact]
    public async Task TheCount_FollowsAdditionsAndDeletions()
    {
        var store = CreateStore();
        var item = Make(Guid.NewGuid().ToString("N"));
        var before = await store.CountAsync();
        try
        {
            await store.AddAsync(item);
            Assert.Equal(before + 1, await store.CountAsync());
        }
        finally { await Cleanup(store, item); }
        Assert.Equal(before, await store.CountAsync());
    }
}
