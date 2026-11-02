using CrmNlQuery.Application.Counting.Services;
using CrmNlQuery.Domain.Metadata.Entities;

namespace CrmNlQuery.Tests.Application.Counting.Services;

/// <summary>
/// Tests narrowing hints built from metadata without model use or row data.
/// </summary>
public class NarrowHintBuilderTests
{
    private static AttributeInfo A(string name, string label, string type, params (int Value, string Label)[] options)
        => new AttributeInfo(name, label, type, options.Select(o => new OptionInfo(o.Value, o.Label)).ToList());

    private static EntityInfo Opportunity(params AttributeInfo[] extra)
    {
        var list = new List<AttributeInfo>
        {
            A("opportunityid", "Opportunity", "Uniqueidentifier"), A("name", "Topic", "String"),
            A("statecode", "Status", "State", (0, "Open"), (1, "Won"), (2, "Lost")),
            A("statuscode", "Status Reason", "Status", (1, "In Progress"), (3, "Won"), (4, "Canceled")),
            A("address1_city", "City", "String"),
            A("createdon", "Created On", "DateTime"),
            A("modifiedon", "Modified On", "DateTime"),
            A("versionnumber", "Version Number", "BigInt"),
            A("estimatedvalue_base", "Est. Revenue (Base)", "Money"),
        };
        list.AddRange(extra);
        return new EntityInfo("opportunity", "opportunities", "Opportunity", "opportunityid", "name", list, new List<RelationshipInfo>());
    }

    private const string NoFilter = "<fetch top=\"500\"><entity name=\"opportunity\"><attribute name=\"name\" /></entity></fetch>";

    /// <summary>
    /// Verifies that status fields come first with option labels, then city then date, at most three.
    /// </summary>
    [Fact]
    public void StatusFieldsComeFirst_WithTheirOptionsAsLabels_ThenCityThenDate_AtMostThree()
    {
        var hints = NarrowHintBuilder.For(NoFilter, Opportunity());

        Assert.Equal(new[] { "statecode", "statuscode", "address1_city" }, hints.Select(h => h.Attribute).ToArray());
        Assert.Equal("options", hints[0].Kind);
        Assert.Equal(new[] { "Open", "Won", "Lost" }, hints[0].Options.ToArray());
        Assert.Equal("Status", hints[0].Label);
        Assert.Equal("text", hints[2].Kind);
        Assert.Empty(hints[2].Options);
    }

    /// <summary>
    /// Verifies that a generic option set comes after city and date but still fills free room.
    /// </summary>
    [Fact]
    public void AGenericOptionSet_ComesAfterTheCityAndTheDate_ButStillFillsFreeRoom()
    {
        var shipping = A("shippingmethodcode", "Shipping Method", "Picklist", (1, "Airborne"), (2, "DHL"), (3, "FedEx"));
        var entity = Opportunity(shipping);

        var all = NarrowHintBuilder.For(NoFilter, entity);
        var freed = NarrowHintBuilder.For("<fetch top=\"500\"><entity name=\"opportunity\"><attribute name=\"name\" />" +
            "<filter><condition attribute=\"statecode\" operator=\"eq\" value=\"0\" /><condition attribute=\"statuscode\" operator=\"eq\" value=\"1\" /></filter></entity></fetch>", entity);

        Assert.DoesNotContain(all, h => h.Attribute == "shippingmethodcode");
        Assert.Equal(new[] { "address1_city", "createdon", "shippingmethodcode" }, freed.Select(h => h.Attribute).ToArray());
    }

    /// <summary>
    /// Verifies that an already filtered field is never suggested.
    /// </summary>
    [Fact]
    public void AnAlreadyFilteredField_IsNeverSuggested()
    {
        var filtered = "<fetch top=\"500\"><entity name=\"opportunity\"><attribute name=\"name\" />" +
            "<filter><condition attribute=\"statecode\" operator=\"eq\" value=\"0\" /></filter></entity></fetch>";

        var hints = NarrowHintBuilder.For(filtered, Opportunity());

        Assert.DoesNotContain(hints, h => h.Attribute == "statecode");
        Assert.Contains(hints, h => h.Attribute == "statuscode");
    }

    /// <summary>
    /// Verifies that a field filtered inside a link is also left out.
    /// </summary>
    [Fact]
    public void AFieldFilteredInsideALink_IsAlsoLeftOut()
    {
        var linked = "<fetch top=\"500\"><entity name=\"opportunity\"><attribute name=\"name\" />" +
            "<link-entity name=\"account\" from=\"accountid\" to=\"customerid\" alias=\"a\"><filter><condition attribute=\"address1_city\" operator=\"eq\" value=\"London\" /></filter></link-entity></entity></fetch>";

        var hints = NarrowHintBuilder.For(linked, Opportunity());

        Assert.DoesNotContain(hints, h => h.Attribute == "address1_city");
    }

    /// <summary>
    /// Verifies that when there are fewer status fields city and date fill the rest.
    /// </summary>
    [Fact]
    public void WhenThereAreFewerStatusFields_CityAndDateFillTheRest()
    {
        var entity = new EntityInfo("lead", "leads", "Lead", "leadid", "fullname",
            new List<AttributeInfo> { A("leadid", "Lead", "Uniqueidentifier"), A("fullname", "Name", "String"), A("address1_city", "City", "String"), A("createdon", "Created On", "DateTime") },
            new List<RelationshipInfo>());

        var hints = NarrowHintBuilder.For(NoFilter.Replace("opportunity", "lead"), entity);

        Assert.Equal(new[] { "address1_city", "createdon" }, hints.Select(h => h.Attribute).ToArray());
        Assert.Equal("date", hints[1].Kind);
    }

    /// <summary>
    /// Verifies that system identity and shadow fields are never suggested.
    /// </summary>
    [Fact]
    public void SystemIdentityAndShadowFields_AreNeverSuggested()
    {
        var hints = NarrowHintBuilder.For(NoFilter, Opportunity());

        Assert.DoesNotContain(hints, h => h.Attribute is "opportunityid" or "name" or "versionnumber" or "modifiedon" or "estimatedvalue_base");
    }

    /// <summary>
    /// Verifies that option sets with too many or too few options are skipped.
    /// </summary>
    [Fact]
    public void OptionSetsWithTooManyOrTooFewOptions_AreSkipped()
    {
        var many = A("bigpick", "Big", "Picklist", Enumerable.Range(1, 12).Select(i => (i, "Option " + i)).ToArray());
        var one = A("single", "Single", "Picklist", (1, "Only"));
        var entity = Opportunity(many, one);

        var hints = NarrowHintBuilder.For(NoFilter, entity);

        Assert.DoesNotContain(hints, h => h.Attribute is "bigpick" or "single");
    }

    /// <summary>
    /// Verifies that a long option list is capped at five.
    /// </summary>
    [Fact]
    public void ALongOptionList_IsCappedAtFive()
    {
        var pick = A("rating", "Rating", "Picklist", Enumerable.Range(1, 8).Select(i => (i, "Level " + i)).ToArray());
        var entity = new EntityInfo("lead", "leads", "Lead", "leadid", "fullname", new List<AttributeInfo> { A("leadid", "Lead", "Uniqueidentifier"), A("fullname", "Name", "String"), pick }, new List<RelationshipInfo>());

        var hints = NarrowHintBuilder.For(NoFilter.Replace("opportunity", "lead"), entity);

        Assert.Equal(5, Assert.Single(hints).Options.Count);
    }

    /// <summary>
    /// Verifies that nothing suitable means no hints.
    /// </summary>
    [Fact]
    public void NothingSuitable_MeansNoHints()
    {
        var entity = new EntityInfo("product", "products", "Product", "productid", "name",
            new List<AttributeInfo> { A("productid", "Product", "Uniqueidentifier"), A("name", "Name", "String"), A("price", "Price", "Money") }, new List<RelationshipInfo>());

        Assert.Empty(NarrowHintBuilder.For(NoFilter.Replace("opportunity", "product"), entity));
    }

    /// <summary>
    /// Verifies that an aggregate or broken query gets no hints.
    /// </summary>
    [Fact]
    public void AnAggregateOrBrokenQuery_GetsNoHints()
    {
        Assert.Empty(NarrowHintBuilder.For("<fetch aggregate=\"true\"><entity name=\"opportunity\" /></fetch>", Opportunity()));
        Assert.Empty(NarrowHintBuilder.For("<fetch><entity", Opportunity()));
    }
}
