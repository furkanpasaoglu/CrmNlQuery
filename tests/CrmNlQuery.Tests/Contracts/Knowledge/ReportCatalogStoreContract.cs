using CrmNlQuery.Application.Reports.Abstractions;
using CrmNlQuery.Domain.Reports.Entities;

namespace CrmNlQuery.Tests.Contracts.Knowledge;

/// <summary>
/// Shared tests that every report catalog store implementation must pass.
/// </summary>
public abstract class ReportCatalogStoreContract
{
    protected abstract IReportCatalogStore CreateStore();

    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    private static string Unique(string prefix) => prefix + Guid.NewGuid().ToString("N").Substring(0, 10);

    private static ReportCatalogEntry Entry(string name) => new ReportCatalogEntry
    {
        Name = name,
        Description = "açıklama ğüşiöç",
        Keywords = new[] { "hesap özeti", "account summary" },
        CrmReportId = Guid.NewGuid().ToString("D"),
        RecordEntity = "account",
        UpdatedBy = "CONTOSO\\admin",
        UpdatedAt = Now,
    };

    /// <summary>
    /// Verifies that an entry is stored and read back with all its fields.
    /// </summary>
    [Fact]
    public async Task AnEntryIsStoredAndReadBack_WithAllFields()
    {
        var store = CreateStore();
        var entry = Entry(Unique("Hesap özeti "));

        await store.UpsertAsync(entry);
        var read = await store.GetAsync(entry.Id);

        Assert.NotNull(read);
        Assert.Equal(entry.Name, read!.Name);
        Assert.Equal(entry.Description, read.Description);
        Assert.Equal(entry.Keywords, read.Keywords);
        Assert.Equal(entry.CrmReportId, read.CrmReportId);
        Assert.Null(read.Url);
        Assert.Equal("account", read.RecordEntity);
        Assert.True(read.Enabled);
        Assert.Equal(entry.UpdatedBy, read.UpdatedBy);
    }

    /// <summary>
    /// Verifies that an external entry keeps its address and parameter.
    /// </summary>
    [Fact]
    public async Task AnExternalEntryKeepsItsAddressAndParameter()
    {
        var store = CreateStore();
        var entry = new ReportCatalogEntry { Name = Unique("Dis "), Url = "https://reports.example/summary", RecordEntity = "account", RecordParam = "accountId", UpdatedBy = "a", UpdatedAt = Now };

        await store.UpsertAsync(entry);
        var read = await store.GetAsync(entry.Id);

        Assert.Equal("https://reports.example/summary", read!.Url);
        Assert.Equal("accountId", read.RecordParam);
        Assert.Null(read.CrmReportId);
    }

    /// <summary>
    /// Verifies that upserting the same ID updates the entry.
    /// </summary>
    [Fact]
    public async Task UpsertingTheSameIdUpdatesTheEntry()
    {
        var store = CreateStore();
        var entry = Entry(Unique("Ilk "));
        await store.UpsertAsync(entry);

        await store.UpsertAsync(entry with { Name = entry.Name + " yeni", Enabled = false });

        var read = await store.GetAsync(entry.Id);
        Assert.EndsWith(" yeni", read!.Name);
        Assert.False(read.Enabled);
        Assert.Single(await store.ListAsync(), e => e.Id == entry.Id);
    }

    /// <summary>
    /// Verifies that delete removes the entry and reports whether it existed.
    /// </summary>
    [Fact]
    public async Task DeleteRemovesTheEntryAndReportsWhetherItExisted()
    {
        var store = CreateStore();
        var entry = Entry(Unique("Sil "));
        await store.UpsertAsync(entry);

        Assert.True(await store.DeleteAsync(entry.Id));
        Assert.False(await store.DeleteAsync(entry.Id));
        Assert.Null(await store.GetAsync(entry.Id));
    }
}
