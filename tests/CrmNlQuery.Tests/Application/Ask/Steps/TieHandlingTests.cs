using System.Xml.Linq;
using CrmNlQuery.Application.Ask.Steps;
using CrmNlQuery.Domain.Ask.Entities;

namespace CrmNlQuery.Tests.Application.Ask.Steps;

/// <summary>
/// Tests tie handling for ranked producer steps: widening top, keeping all tied rows and noting it in the answer.
/// </summary>
public class TieHandlingTests
{
    private static string Aggregate(string topAttribute, bool descending = true) =>
        $"<fetch aggregate=\"true\"{topAttribute}><entity name=\"opportunity\"><attribute name=\"opportunityid\" aggregate=\"count\" alias=\"n\" />" +
        $"<attribute name=\"customerid\" groupby=\"true\" alias=\"_rowid\" /><order alias=\"n\" descending=\"{(descending ? "true" : "false")}\" /></entity></fetch>";

    private static ResultTable Table(string column, string[] values, string[]? ids = null) =>
        new ResultTable(new List<string> { column }, values.Select(v => (IReadOnlyList<string>)new List<string> { v }).ToList(), ids?.ToList());

    /// <summary>
    /// Verifies that a generic top500 on a ranked producer is treated as no top when top one is assumed.
    /// </summary>
    [Fact]
    public void AGenericTop500OnARankedProducer_IsTreatedAsNoTop_WhenTopOneIsAssumed()
    {
        var plan = TieHandling.Prepare(Aggregate(" top=\"500\""), assumeTopOne: true)!;

        Assert.Equal(1, plan.N);
        Assert.Contains("top=\"" + TieHandling.MaxRows + "\"", plan.FetchXml);
    }

    /// <summary>
    /// Verifies that a ranked aggregate gets a wider top and the measure column is found.
    /// </summary>
    [Fact]
    public void ARankedAggregate_GetsAWiderTop_AndTheMeasureColumnIsFound()
    {
        var plan = TieHandling.Prepare(Aggregate(" top=\"1\""))!;

        Assert.Equal(1, plan.N);
        Assert.Equal("n", plan.MeasureColumn);
        Assert.Equal(TieHandling.MaxRows.ToString(), (string?)XDocument.Parse(plan.FetchXml).Root!.Attribute("top"));
        Assert.Equal("true", (string?)XDocument.Parse(plan.FetchXml).Root!.Attribute("aggregate"));
    }

    /// <summary>
    /// Verifies that without a top or without an order there is nothing to rank.
    /// </summary>
    [Fact]
    public void WithoutATopOrWithoutAnOrder_ThereIsNothingToRank()
    {
        Assert.Null(TieHandling.Prepare(Aggregate("")));
        Assert.Null(TieHandling.Prepare("<fetch top=\"3\"><entity name=\"account\"><attribute name=\"name\" /></entity></fetch>"));
    }

    /// <summary>
    /// Verifies that when the producer has an order but no top one record is assumed so ties are handled not everything.
    /// </summary>
    [Fact]
    public void WhenTheProducerHasAnOrderButNoTop_OneRecordIsAssumed_SoTiesAreHandledNotEverything()
    {
        var plan = TieHandling.Prepare(Aggregate(""), assumeTopOne: true)!;

        Assert.Equal(1, plan.N);
        Assert.Equal(TieHandling.MaxRows.ToString(), (string?)XDocument.Parse(plan.FetchXml).Root!.Attribute("top"));
        var outcome = TieHandling.Resolve(plan, Table("n", new[] { "1", "1", "3" }, new[] { "a", "b", "c" }));
        Assert.Equal(2, outcome.Table.Rows.Count);
        Assert.NotNull(outcome.Note);
    }

    /// <summary>
    /// Verifies that assuming a top of one needs an order and is off by default.
    /// </summary>
    [Fact]
    public void AssumingATopOfOne_NeedsAnOrder_AndIsOffByDefault()
    {
        var noOrder = "<fetch aggregate=\"true\"><entity name=\"opportunity\"><attribute name=\"opportunityid\" aggregate=\"count\" alias=\"n\" /><attribute name=\"customerid\" groupby=\"true\" alias=\"_rowid\" /></entity></fetch>";

        Assert.Null(TieHandling.Prepare(noOrder, assumeTopOne: true));
        Assert.Null(TieHandling.Prepare(Aggregate("")));
    }

    /// <summary>
    /// Verifies that a top of the wide limit or more is left alone.
    /// </summary>
    [Fact]
    public void ATopOfTheWideLimitOrMore_IsLeftAlone()
    {
        Assert.Null(TieHandling.Prepare(Aggregate(" top=\"" + TieHandling.MaxRows + "\"")));
        Assert.Null(TieHandling.Prepare(Aggregate(" top=\"500\"")));
    }

    /// <summary>
    /// Verifies that an order alias inside a link entity is found and the column is matched through the link prefix.
    /// </summary>
    [Fact]
    public void AnOrderAliasInsideALinkEntity_IsFound_AndTheColumnIsMatchedThroughTheLinkPrefix()
    {
        var xml = "<fetch aggregate=\"true\" top=\"1\"><entity name=\"account\"><attribute name=\"accountid\" groupby=\"true\" alias=\"_rowid\" />" +
            "<link-entity name=\"opportunity\" from=\"customerid\" to=\"accountid\" alias=\"o\"><attribute name=\"opportunityid\" aggregate=\"count\" alias=\"n\" /><order alias=\"n\" descending=\"true\" /></link-entity></entity></fetch>";

        var plan = TieHandling.Prepare(xml)!;
        var table = Table("o.n", new[] { "3", "1", "1" }, new[] { "a", "b", "c" });
        var outcome = TieHandling.Resolve(plan, table);

        Assert.Equal("n", plan.MeasureColumn);
        Assert.Single(outcome.Table.Rows);
        Assert.Null(outcome.Note);
    }

    /// <summary>
    /// Verifies that an aggregate ordered by a column that is not a measure is left alone.
    /// </summary>
    [Fact]
    public void AnAggregateOrderedByAColumnThatIsNotAMeasure_IsLeftAlone()
    {
        var xml = "<fetch aggregate=\"true\" top=\"1\"><entity name=\"opportunity\"><attribute name=\"opportunityid\" aggregate=\"count\" alias=\"n\" />" +
            "<attribute name=\"customerid\" groupby=\"true\" alias=\"_rowid\" /><order attribute=\"customerid\" /></entity></fetch>";

        Assert.Null(TieHandling.Prepare(xml));
    }

    /// <summary>
    /// Verifies that a plain ranking makes sure the ordered attribute is selected so ties can be seen.
    /// </summary>
    [Fact]
    public void APlainRanking_MakesSureTheOrderedAttributeIsSelected_SoTiesCanBeSeen()
    {
        var xml = "<fetch top=\"1\"><entity name=\"account\"><attribute name=\"name\" /><order attribute=\"numberofemployees\" descending=\"true\" /></entity></fetch>";

        var plan = TieHandling.Prepare(xml)!;

        var entity = XDocument.Parse(plan.FetchXml).Root!.Element("entity")!;
        Assert.Contains(entity.Elements("attribute"), a => (string?)a.Attribute("name") == "numberofemployees");
        Assert.Equal("numberofemployees", plan.MeasureColumn);
        Assert.Equal(TieHandling.MaxRows.ToString(), (string?)XDocument.Parse(plan.FetchXml).Root!.Attribute("top"));
    }

    /// <summary>
    /// Verifies that a plain ranking whose ordered attribute is already selected gets no duplicate.
    /// </summary>
    [Fact]
    public void APlainRanking_WhoseOrderedAttributeIsAlreadySelected_GetsNoDuplicate()
    {
        var xml = "<fetch top=\"1\"><entity name=\"account\"><attribute name=\"name\" /><attribute name=\"numberofemployees\" /><order attribute=\"numberofemployees\" descending=\"true\" /></entity></fetch>";

        var plan = TieHandling.Prepare(xml)!;

        var attributes = XDocument.Parse(plan.FetchXml).Root!.Element("entity")!.Elements("attribute").ToList();
        Assert.Equal(2, attributes.Count);
    }

    /// <summary>
    /// Verifies that an aliased ordered attribute is read through its alias.
    /// </summary>
    [Fact]
    public void AnAliasedOrderedAttribute_IsReadThroughItsAlias()
    {
        var xml = "<fetch top=\"1\"><entity name=\"account\"><attribute name=\"name\" /><attribute name=\"numberofemployees\" alias=\"staff\" /><order attribute=\"numberofemployees\" descending=\"true\" /></entity></fetch>";

        Assert.Equal("staff", TieHandling.Prepare(xml)!.MeasureColumn);
    }

    /// <summary>
    /// Verifies that a unique winner keeps one row and no note.
    /// </summary>
    [Fact]
    public void AUniqueWinner_KeepsOneRow_AndNoNote()
    {
        var plan = TieHandling.Prepare(Aggregate(" top=\"1\""))!;
        var table = Table("n", new[] { "3", "1", "1", "1" }, new[] { "a", "b", "c", "d" });

        var outcome = TieHandling.Resolve(plan, table);

        Assert.Equal(new[] { "3" }, outcome.Table.Rows.Select(r => r[0]).ToArray());
        Assert.Equal(new[] { "a" }, outcome.Table.RowIds);
        Assert.Null(outcome.Note);
    }

    /// <summary>
    /// Verifies that a tie at the boundary keeps every tied row and says so.
    /// </summary>
    [Fact]
    public void ATieAtTheBoundary_KeepsEveryTiedRow_AndSaysSo()
    {
        var plan = TieHandling.Prepare(Aggregate(" top=\"1\"", descending: false))!;
        var table = Table("n", new[] { "1", "1", "1", "1", "1", "1", "1", "3" }, new[] { "a", "b", "c", "d", "e", "f", "g", "h" });

        var outcome = TieHandling.Resolve(plan, table);

        Assert.Equal(7, outcome.Table.Rows.Count);
        Assert.Equal(7, outcome.Table.RowIds!.Count);
        Assert.DoesNotContain("h", outcome.Table.RowIds);
        Assert.NotNull(outcome.Note);
        Assert.Contains("7", outcome.Note);
        Assert.Contains("n", outcome.Note);
        Assert.Contains("1", outcome.Note);
    }

    /// <summary>
    /// Verifies that top two with a tie across the boundary keeps the tied rows beyond n.
    /// </summary>
    [Fact]
    public void TopTwo_WithATieAcrossTheBoundary_KeepsTheTiedRowsBeyondN()
    {
        var plan = TieHandling.Prepare(Aggregate(" top=\"2\""))!;
        var table = Table("n", new[] { "5", "4", "4", "4", "3" });

        var outcome = TieHandling.Resolve(plan, table);

        Assert.Equal(new[] { "5", "4", "4", "4" }, outcome.Table.Rows.Select(r => r[0]).ToArray());
        Assert.NotNull(outcome.Note);
    }

    /// <summary>
    /// Verifies that top two without a tie across the boundary keeps exactly two and no note.
    /// </summary>
    [Fact]
    public void TopTwo_WithoutATieAcrossTheBoundary_KeepsExactlyTwo_AndNoNote()
    {
        var plan = TieHandling.Prepare(Aggregate(" top=\"2\""))!;
        var table = Table("n", new[] { "5", "4", "3" });

        var outcome = TieHandling.Resolve(plan, table);

        Assert.Equal(2, outcome.Table.Rows.Count);
        Assert.Null(outcome.Note);
    }

    /// <summary>
    /// Verifies that fewer rows than n are kept as they are.
    /// </summary>
    [Fact]
    public void FewerRowsThanN_AreKeptAsTheyAre()
    {
        var plan = TieHandling.Prepare(Aggregate(" top=\"5\""))!;
        var table = Table("n", new[] { "3", "2" });

        var outcome = TieHandling.Resolve(plan, table);

        Assert.Equal(2, outcome.Table.Rows.Count);
        Assert.Null(outcome.Note);
    }

    /// <summary>
    /// Verifies that when the measure column is missing from the result the table is returned untouched.
    /// </summary>
    [Fact]
    public void WhenTheMeasureColumnIsMissingFromTheResult_TheTableIsReturnedUntouched()
    {
        var plan = TieHandling.Prepare(Aggregate(" top=\"1\""))!;
        var table = Table("other", new[] { "3", "1" });

        var outcome = TieHandling.Resolve(plan, table);

        Assert.Same(table, outcome.Table);
        Assert.Null(outcome.Note);
    }
}
