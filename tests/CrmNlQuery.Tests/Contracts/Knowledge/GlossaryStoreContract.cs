using CrmNlQuery.Application.Glossary.Abstractions;
using CrmNlQuery.Application.Glossary.Exceptions;
using CrmNlQuery.Domain.Glossary.Constants;
using CrmNlQuery.Domain.Glossary.Entities;

namespace CrmNlQuery.Tests.Contracts.Knowledge;

/// <summary>
/// Shared contract tests every glossary store implementation, in-memory and PostgreSQL, must pass.
/// </summary>
public abstract class GlossaryStoreContract
{
    protected abstract IGlossaryStore CreateStore();

    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private static string Unique(string prefix) => prefix + Guid.NewGuid().ToString("N").Substring(0, 10);

    private static GlossaryEntry Filter(string term, string? second = null) => new GlossaryEntry
    {
        Kind = GlossaryKind.Filter,
        Entity = "account",
        Terms = second is null ? new[] { new GlossaryTerm(term, "tr") } : new[] { new GlossaryTerm(term, "tr"), new GlossaryTerm(second, "en") },
        Conditions = new[] { new GlossaryCondition("numberofemployees", "gt", new[] { "1000" }), new GlossaryCondition("statecode", "in", new[] { "0", "1" }) },
        Description = "açıklama ğüşiöç",
        CreatedBy = "CONTOSO\\admin", CreatedAt = Now, ModifiedBy = "CONTOSO\\admin", ModifiedAt = Now,
    };

    /// <summary>
    /// Verifies that an entry is stored and read back with all fields.
    /// </summary>
    [Fact]
    public async Task AnEntryIsStoredAndReadBack_WithAllFields()
    {
        var store = CreateStore();
        var entry = Filter(Unique("buyuk"), Unique("large"));
        await store.AddAsync(entry);

        var read = await store.GetAsync(entry.Id);

        Assert.NotNull(read);
        Assert.Equal(entry.Kind, read!.Kind);
        Assert.Equal(entry.Entity, read.Entity);
        Assert.Equal(entry.Terms, read.Terms);
        Assert.Equal(2, read.Conditions.Count);
        Assert.Equal(new[] { "0", "1" }, read.Conditions[1].Values);
        Assert.Equal("açıklama ğüşiöç", read.Description);
        Assert.True(read.Enabled);
        Assert.Equal(Now, read.CreatedAt);
        Assert.Contains(await store.ListAsync(), e => e.Id == entry.Id);
    }

    /// <summary>
    /// Verifies that a duplicate term is rejected across entries and by folded form.
    /// </summary>
    [Fact]
    public async Task ADuplicateTerm_IsRejected_AcrossEntries_AndByFoldedForm()
    {
        var store = CreateStore();
        var term = Unique("Kobi");
        var first = Filter(term);
        await store.AddAsync(first);

        var second = Filter(term.ToUpperInvariant());
        var ex = await Assert.ThrowsAsync<GlossaryDuplicateTermException>(() => store.AddAsync(second));

        Assert.Equal(first.Id, ex.OtherEntryId);
        Assert.Null(await store.GetAsync(second.Id));
    }

    /// <summary>
    /// Verifies that an entry cannot repeat its own term.
    /// </summary>
    [Fact]
    public async Task AnEntry_CannotRepeatItsOwnTerm()
    {
        var store = CreateStore();
        var term = Unique("same");
        await Assert.ThrowsAsync<GlossaryDuplicateTermException>(() => store.AddAsync(Filter(term, term)));
    }

    /// <summary>
    /// Verifies that an update replaces terms and conditions and may keep its own terms.
    /// </summary>
    [Fact]
    public async Task AnUpdate_ReplacesTermsAndConditions_AndMayKeepItsOwnTerms()
    {
        var store = CreateStore();
        var term = Unique("vip");
        var entry = Filter(term);
        await store.AddAsync(entry);
        var other = Unique("yeni");

        var changed = entry with { Terms = new[] { new GlossaryTerm(term, "tr"), new GlossaryTerm(other, "tr") }, Enabled = false, Description = "d2" };
        Assert.True(await store.UpdateAsync(changed));

        var read = await store.GetAsync(entry.Id);
        Assert.Equal(2, read!.Terms.Count);
        Assert.False(read.Enabled);
        Assert.Equal("d2", read.Description);
        Assert.False(await store.UpdateAsync(Filter(Unique("none"))));
    }

    /// <summary>
    /// Verifies that an update cannot take another entrys term.
    /// </summary>
    [Fact]
    public async Task AnUpdate_CannotTakeAnotherEntrysTerm()
    {
        var store = CreateStore();
        var a = Filter(Unique("a"));
        var b = Filter(Unique("b"));
        await store.AddAsync(a);
        await store.AddAsync(b);

        await Assert.ThrowsAsync<GlossaryDuplicateTermException>(() => store.UpdateAsync(b with { Terms = a.Terms }));

        Assert.Equal(b.Terms, (await store.GetAsync(b.Id))!.Terms);
    }

    /// <summary>
    /// Verifies that deleting removes the entry its terms and its stats and frees the term.
    /// </summary>
    [Fact]
    public async Task Deleting_RemovesTheEntry_ItsTermsAndItsStats_AndFreesTheTerm()
    {
        var store = CreateStore();
        var term = Unique("sil");
        var entry = Filter(term);
        await store.AddAsync(entry);
        await store.RecordAsync(entry.Id, false, Now);

        Assert.True(await store.DeleteAsync(entry.Id));

        Assert.Null(await store.GetAsync(entry.Id));
        Assert.DoesNotContain(await store.StatsAsync(), s => s.EntryId == entry.Id);
        await store.AddAsync(Filter(term));
        Assert.False(await store.DeleteAsync(entry.Id));
    }

    /// <summary>
    /// Verifies that counters count applied and not applied and remember when it last failed.
    /// </summary>
    [Fact]
    public async Task Counters_CountAppliedAndNotApplied_AndRemember_WhenItLastFailed()
    {
        var store = CreateStore();
        var entry = Filter(Unique("say"));
        await store.AddAsync(entry);

        await store.RecordAsync(entry.Id, true, Now);
        await store.RecordAsync(entry.Id, true, Now);
        await store.RecordAsync(entry.Id, false, Now.AddHours(1));
        await store.RecordAsync(entry.Id, true, Now.AddHours(2));

        var stats = (await store.StatsAsync()).Single(s => s.EntryId == entry.Id);
        Assert.Equal(3, stats.Applied);
        Assert.Equal(1, stats.NotApplied);
        Assert.Equal(Now.AddHours(1), stats.LastNotAppliedAt);
    }
}
