using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.Explain.Services;
using CrmNlQuery.Application.Localization.Services;
using CrmNlQuery.Domain.Metadata.Entities;

namespace CrmNlQuery.Tests.Application.Explain.Services;

/// <summary>
/// Tests readable table header labels: plain fields, linked tables, aggregate measures, date grouping and alignment.
/// </summary>
[Collection("RequestLanguage")]
public class ColumnLabelerTests
{
    private sealed class Schema : IMetadataSource
    {
        private static AttributeInfo A(string name, string label, string type = "String") => new AttributeInfo(name, label, type, new List<OptionInfo>());

        private readonly Dictionary<string, EntityInfo> _entities = new Dictionary<string, EntityInfo>
        {
            ["account"] = new EntityInfo("account", "accounts", "Account", "accountid", "name",
                new List<AttributeInfo> { A("accountid", "Account", "Uniqueidentifier"), A("name", "Account Name"), A("telephone1", "Business Phone"), A("revenue", "Annual Revenue", "Money"), A("createdon", "Created On", "DateTime") },
                new List<RelationshipInfo>()),
            ["contact"] = new EntityInfo("contact", "contacts", "Contact", "contactid", "fullname",
                new List<AttributeInfo> { A("contactid", "Contact", "Uniqueidentifier"), A("fullname", "Full Name"), A("jobtitle", "Job Title") },
                new List<RelationshipInfo>()),
        };

        /// <summary>
        /// Test schema source that lists no entities, as the column labeler tests only need single entity lookups.
        /// </summary>
        public Task<IReadOnlyList<EntitySummary>> ListEntitiesAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<EntitySummary>>(new List<EntitySummary>());
        /// <summary>
        /// Returns canned entity metadata by logical name for the column labeler tests, so no CRM schema is read.
        /// </summary>
        public Task<EntityInfo?> GetEntityAsync(string logicalName, CancellationToken ct = default) => Task.FromResult(_entities.TryGetValue(logicalName, out var e) ? e : null);
        /// <summary>
        /// Does nothing in this test schema source, since schema refresh is irrelevant to the column labeler tests.
        /// </summary>
        public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private static Task<IReadOnlyList<string>?> Label(string fetch, params string[] columns) => ColumnLabeler.TryLabelAsync(fetch, columns, new Schema());

    /// <summary>
    /// Verifies that plain fields use their metadata labels.
    /// </summary>
    [Fact]
    public async Task PlainFields_UseTheirMetadataLabels()
    {
        var labels = await Label("<fetch><entity name='account'><attribute name='name'/><attribute name='telephone1'/></entity></fetch>", "name", "telephone1");

        Assert.Equal(new[] { "Account Name", "Business Phone" }, labels);
    }

    /// <summary>
    /// Verifies that a linked table column names its table.
    /// </summary>
    [Fact]
    public async Task ALinkedTableColumn_NamesItsTable()
    {
        var fetch = "<fetch><entity name='account'><attribute name='name'/><link-entity name='contact' from='parentcustomerid' to='accountid' alias='c'><attribute name='fullname'/></link-entity></entity></fetch>";

        var labels = await Label(fetch, "name", "c.fullname");

        Assert.Equal(new[] { "Account Name", "Contact · Full Name" }, labels);
    }

    /// <summary>
    /// Verifies that aggregate columns get measure and group labels.
    /// </summary>
    [Fact]
    public async Task AggregateColumns_GetMeasureAndGroupLabels()
    {
        RequestLanguage.Set("en");
        var fetch = "<fetch aggregate='true'><entity name='account'><attribute name='createdon' groupby='true' dategrouping='month' alias='m'/><attribute name='accountid' aggregate='count' alias='n'/><attribute name='revenue' aggregate='sum' alias='total'/></entity></fetch>";

        var labels = await Label(fetch, "m", "n", "total");

        Assert.Equal(new[] { "Created On (month)", "Count", "Total Annual Revenue" }, labels);
    }

    /// <summary>
    /// Verifies that the labels follow the request language.
    /// </summary>
    [Fact]
    public async Task TheLabelsFollowTheRequestLanguage()
    {
        try
        {
            RequestLanguage.Set("tr");
            var fetch = "<fetch aggregate='true'><entity name='account'><attribute name='accountid' aggregate='count' alias='n'/><attribute name='revenue' aggregate='avg' alias='a'/></entity></fetch>";

            var labels = await Label(fetch, "n", "a");

            Assert.Equal(new[] { "Sayı", "Annual Revenue ortalaması" }, labels);
        }
        finally { RequestLanguage.Set("en"); }
    }

    /// <summary>
    /// Verifies that an unknown attribute keeps its key.
    /// </summary>
    [Fact]
    public async Task AnUnknownAttribute_KeepsItsKey()
    {
        var labels = await Label("<fetch><entity name='account'><attribute name='name'/><attribute name='customeridname'/></entity></fetch>", "name", "customeridname");

        Assert.Equal(new[] { "Account Name", "customeridname" }, labels);
    }

    /// <summary>
    /// Verifies that the hidden row ID column is skipped so the labels stay aligned.
    /// </summary>
    [Fact]
    public async Task TheHiddenRowIdColumn_IsSkipped_SoTheLabelsStayAligned()
    {
        var labels = await Label("<fetch><entity name='account'><attribute name='accountid' alias='_rowid'/><attribute name='name'/></entity></fetch>", "name");

        Assert.Equal(new[] { "Account Name" }, labels);
    }

    /// <summary>
    /// Verifies that a column count mismatch gives no labels at all.
    /// </summary>
    [Fact]
    public async Task AColumnCountMismatch_GivesNoLabelsAtAll()
    {
        Assert.Null(await Label("<fetch><entity name='account'><attribute name='name'/></entity></fetch>", "name", "extra"));
        Assert.Null(await Label("not xml", "x"));
    }
}
