using CrmNlQuery.Application.Suggestions.Services;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Domain.Metadata.Entities;

namespace CrmNlQuery.Tests.Application.Suggestions.Services;

/// <summary>
/// Unit tests for the suggestion builder, which proposes narrowing, grouping, related-table, time and sorting follow-ups for list results.
/// </summary>
public class SuggestionBuilderTests
{
    private static AttributeInfo A(string name, string type, params (int Value, string Label)[] options)
        => new AttributeInfo(name, name == "statecode" ? "Status" : name, type, options.Select(o => new OptionInfo(o.Value, o.Label)).ToList());

    private static EntityInfo Opportunity() => new EntityInfo("opportunity", "opportunities", "Opportunity", "opportunityid", "name",
        new List<AttributeInfo>
        {
            A("opportunityid", "Uniqueidentifier"), A("name", "String"),
            A("statecode", "State", (0, "Open"), (1, "Won"), (2, "Lost")),
            A("prioritycode", "Picklist", (1, "High"), (2, "Normal"), (3, "Low")),
            A("ownerid", "Owner"), A("customerid", "Customer"), A("createdby", "Lookup"),
            A("estimatedvalue", "Money"), A("createdon", "DateTime"), A("versionnumber", "BigInt"),
        },
        new List<RelationshipInfo> { new RelationshipInfo("incident_opp", "incident", "opportunityid", "opportunity", "opportunityid") });

    private static readonly IReadOnlyList<EntitySummary> Entities = new List<EntitySummary>
    {
        new EntitySummary("opportunity", "opportunities", "Opportunity"), new EntitySummary("incident", "incidents", "Case"),
    };

    private const string Plain = "<fetch top=\"500\"><entity name=\"opportunity\"><attribute name=\"name\" /><attribute name=\"statecode\" /><attribute name=\"prioritycode\" /></entity></fetch>";

    private static ResultTable Table(params string[][] rows)
        => new ResultTable(new[] { "name", "statecode", "prioritycode" }, rows.Select(r => (IReadOnlyList<string>)r).ToList());

    private static ResultTable ManyRows(int n)
        => Table(Enumerable.Range(0, n).Select(i => new[] { "O" + i, i % 3 == 0 ? "Won" : "Open", i % 2 == 0 ? "High" : "Low" }).ToArray());

    /// <summary>
    /// Verifies that list result gets narrow and related suggestions at most four.
    /// </summary>
    [Fact]
    public void ListResult_GetsNarrowAndRelatedSuggestions_AtMostFour()
    {
        var all = SuggestionBuilder.For(Plain, Opportunity(), ManyRows(30), false, Entities);

        Assert.True(all.Count <= SuggestionBuilder.Max);
        Assert.Equal("narrow", all[0].Kind);
        Assert.Equal("narrow:statecode:0", all[0].Id);
        Assert.Equal("Status: Open", all[0].Label);
        Assert.Contains(all, s => s.Kind == "related" && s.Question!.Contains("Case"));
    }

    /// <summary>
    /// Verifies that an empty or constant column is not offered for grouping.
    /// </summary>
    [Fact]
    public void AnEmptyOrConstantColumn_IsNotOfferedForGrouping()
    {
        var allHigh = Table(Enumerable.Range(0, 20).Select(i => new[] { "O" + i, "Open", "High" }).ToArray());

        var all = SuggestionBuilder.For(Plain, Opportunity(), allHigh, false, Entities);

        Assert.DoesNotContain(all, s => s.Id == "group:prioritycode");
    }

    /// <summary>
    /// Verifies that a not displayed field is only groupable when it is a status field.
    /// </summary>
    [Fact]
    public void ANotDisplayedField_IsOnlyGroupable_WhenItIsAStatusField()
    {
        var onlyNames = new ResultTable(new[] { "name" }, Enumerable.Range(0, 20).Select(i => (IReadOnlyList<string>)new[] { "O" + i }).ToList());
        var fetch = "<fetch top=\"500\"><entity name=\"opportunity\"><attribute name=\"name\" /></entity></fetch>";

        var all = SuggestionBuilder.For(fetch, Opportunity(), onlyNames, false, Entities);

        Assert.Contains(all, s => s.Id == "group:statecode");
        Assert.DoesNotContain(all, s => s.Id == "group:prioritycode");
    }

    /// <summary>
    /// Verifies that a single distinct value is not worth narrowing.
    /// </summary>
    [Fact]
    public void ASingleDistinctValue_IsNotWorthNarrowing()
    {
        var table = Table(new[] { "A", "Open", "High" }, new[] { "B", "Open", "Low" });

        var all = SuggestionBuilder.For(Plain, Opportunity(), table, false, Entities);

        Assert.DoesNotContain(all, s => s.Id.StartsWith("narrow:statecode"));
    }

    /// <summary>
    /// Verifies that already filtered fields are not suggested.
    /// </summary>
    [Fact]
    public void AlreadyFilteredFields_AreNotSuggested()
    {
        var fetch = "<fetch><entity name=\"opportunity\"><attribute name=\"name\" /><attribute name=\"statecode\" /><attribute name=\"prioritycode\" />" +
                    "<filter><condition attribute=\"statecode\" operator=\"eq\" value=\"0\" /><condition attribute=\"createdon\" operator=\"last-x-days\" value=\"7\" /></filter></entity></fetch>";

        var all = SuggestionBuilder.For(fetch, Opportunity(), ManyRows(30), false, Entities);

        Assert.DoesNotContain(all, s => s.Id.Contains("statecode"));
        Assert.DoesNotContain(all, s => s.Kind == "time");
    }

    /// <summary>
    /// Verifies that closed fields never appear because they are not in the metadata.
    /// </summary>
    [Fact]
    public void ClosedFields_NeverAppear_BecauseTheyAreNotInTheMetadata()
    {
        var withoutAmount = Opportunity() with { Attributes = Opportunity().Attributes.Where(a => a.LogicalName != "estimatedvalue").ToList() };

        var all = SuggestionBuilder.For(Plain, withoutAmount, ManyRows(30), false, Entities);

        Assert.DoesNotContain(all, s => s.Id.Contains("estimatedvalue"));
    }

    /// <summary>
    /// Verifies that sort suggestion needs more than ten rows or a truncated result.
    /// </summary>
    [Fact]
    public void SortSuggestion_NeedsMoreThanTenRows_OrATruncatedResult()
    {
        var small = SuggestionBuilder.For(Plain, Opportunity(), ManyRows(5), false, Entities);
        var big = SuggestionBuilder.For(Plain, Opportunity(), ManyRows(30), false, Entities.Take(0).ToList());
        var truncated = SuggestionBuilder.For(Plain, Opportunity(), ManyRows(5), true, Entities.Take(0).ToList());

        Assert.DoesNotContain(small, s => s.Kind == "sort");
        Assert.Contains(big, s => s.Id == "sort:estimatedvalue");
        Assert.Contains(truncated, s => s.Kind == "sort");
    }

    /// <summary>
    /// Verifies that system fields and creator lookups are not suggested.
    /// </summary>
    [Fact]
    public void SystemFieldsAndCreatorLookups_AreNotSuggested()
    {
        var all = SuggestionBuilder.For(Plain, Opportunity(), ManyRows(30), false, Entities);

        Assert.DoesNotContain(all, s => s.Id.Contains("versionnumber") || s.Id.Contains("createdby"));
    }

    /// <summary>
    /// Verifies that aggregates empty results get no suggestions.
    /// </summary>
    [Fact]
    public void Aggregates_EmptyResults_GetNoSuggestions()
    {
        var aggregate = "<fetch aggregate=\"true\"><entity name=\"opportunity\"><attribute name=\"statecode\" groupby=\"true\" alias=\"s\" /></entity></fetch>";

        Assert.Empty(SuggestionBuilder.For(aggregate, Opportunity(), ManyRows(30), false, Entities));
        Assert.Empty(SuggestionBuilder.For(Plain, Opportunity(), Table(), false, Entities));
    }

    /// <summary>
    /// Verifies that related suggestion is skipped when the table is not available or already linked.
    /// </summary>
    [Fact]
    public void RelatedSuggestion_IsSkipped_WhenTheTableIsNotAvailable_OrAlreadyLinked()
    {
        var none = SuggestionBuilder.For(Plain, Opportunity(), ManyRows(30), false, new List<EntitySummary>());
        var linked = "<fetch><entity name=\"opportunity\"><attribute name=\"name\" /><link-entity name=\"incident\" from=\"opportunityid\" to=\"opportunityid\" alias=\"i\" /></entity></fetch>";
        var already = SuggestionBuilder.For(linked, Opportunity(), ManyRows(30), false, Entities);

        Assert.DoesNotContain(none, s => s.Kind == "related");
        Assert.DoesNotContain(already, s => s.Kind == "related");
    }

    /// <summary>
    /// Verifies that a truncated result without that column falls back to metadata options.
    /// </summary>
    [Fact]
    public void ATruncatedResultWithoutThatColumn_FallsBackToMetadataOptions()
    {
        var fetch = "<fetch top=\"500\"><entity name=\"opportunity\"><attribute name=\"name\" /></entity></fetch>";
        var table = new ResultTable(new[] { "name" }, Enumerable.Range(0, 20).Select(i => (IReadOnlyList<string>)new[] { "O" + i }).ToList());

        var all = SuggestionBuilder.For(fetch, Opportunity(), table, true, Entities);

        Assert.Equal(new[] { "narrow:statecode:0", "narrow:statecode:1" }, all.Where(s => s.Kind == "narrow").Select(s => s.Id));
    }
}
