using CrmNlQuery.Application.Observability.AccessLog;

namespace CrmNlQuery.Tests.Application.Observability.AccessLog;

/// <summary>
/// Tests that the access log gets only table names: the root table and link-entity tables.
/// </summary>
public class FetchXmlTablesTests
{
    /// <summary>
    /// Verifies that the root table is listed.
    /// </summary>
    [Fact]
    public void TheRootTable_IsListed()
    {
        Assert.Equal(new[] { "product" }, FetchXmlTables.From("<fetch top=\"50\"><entity name=\"product\"><attribute name=\"name\" /></entity></fetch>"));
    }

    /// <summary>
    /// Verifies that linked tables follow the root also when nested.
    /// </summary>
    [Fact]
    public void LinkedTables_FollowTheRoot_AlsoWhenNested()
    {
        var xml = "<fetch><entity name=\"contract\"><link-entity name=\"account\" from=\"accountid\" to=\"customerid\">" +
                  "<link-entity name=\"contact\" from=\"parentcustomerid\" to=\"accountid\" /></link-entity></entity></fetch>";

        Assert.Equal(new[] { "contract", "account", "contact" }, FetchXmlTables.From(xml));
    }

    /// <summary>
    /// Verifies that a repeated table is listed once and names are lower cased.
    /// </summary>
    [Fact]
    public void ARepeatedTable_IsListedOnce_AndNamesAreLowerCased()
    {
        var xml = "<fetch><entity name=\"Account\"><link-entity name=\"contact\" from=\"a\" to=\"b\" /><link-entity name=\"CONTACT\" from=\"a\" to=\"b\" /></entity></fetch>";

        Assert.Equal(new[] { "account", "contact" }, FetchXmlTables.From(xml));
    }

    /// <summary>
    /// Verifies that no filter value or ID or column ever appears.
    /// </summary>
    [Fact]
    public void NoFilterValueOrIdOrColumn_EverAppears()
    {
        var xml = "<fetch><entity name=\"account\"><attribute name=\"secretcolumn\" />" +
                  "<filter><condition attribute=\"name\" operator=\"eq\" value=\"Örnekova Holding\" /><condition attribute=\"accountid\" operator=\"in\"><value>aaaaaaaa-0000-0000-0000-000000000001</value></condition></filter></entity></fetch>";

        var joined = string.Join(",", FetchXmlTables.From(xml));

        Assert.Equal("account", joined);
    }

    /// <summary>
    /// Verifies that broken or empty input gives an empty list never throws.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not xml at all")]
    [InlineData("<fetch><entity></fetch>")]
    [InlineData("<fetch />")]
    public void BrokenOrEmptyInput_GivesAnEmptyList_NeverThrows(string xml)
    {
        Assert.Empty(FetchXmlTables.From(xml));
    }
}
