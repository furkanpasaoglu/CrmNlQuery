using CrmNlQuery.Application.Ask.Planning.Validation;
using CrmNlQuery.Tests.Support;
using CrmNlQuery.Domain.Metadata.Entities;

namespace CrmNlQuery.Tests.Application.Ask.Planning.Validation;

/// <summary>
/// Tests that the plan validator rejects non-numeric values on integer, decimal and money fields before the CRM returns a 400.
/// </summary>
public class PlanValidatorNumericTests
{
    private static AttributeInfo A(string name, string type) => new AttributeInfo(name, name, type, new List<OptionInfo>());

    private static Task<ValidationResult> Check(string condition)
    {
        var account = new EntityInfo("account", "accounts", "Account", "accountid", "name",
            new List<AttributeInfo> { A("accountid", "Uniqueidentifier"), A("name", "String"), A("numberofemployees", "Integer"), A("revenue", "Money"), A("score", "Decimal"), A("rank", "Double"), A("big", "BigInt") },
            new List<RelationshipInfo>());
        var xml = "<fetch><entity name=\"account\"><attribute name=\"name\" /><filter>" + condition + "</filter></entity></fetch>";
        return new PlanValidator(new FakeMetadataSource(account)).ValidateFetchXmlAsync(xml);
    }

    /// <summary>
    /// Verifies that a numeric value on a numeric field passes.
    /// </summary>
    [Theory]
    [InlineData("numberofemployees", "gt", "1000")]
    [InlineData("revenue", "ge", "2500.50")]
    [InlineData("score", "lt", "-3.5")]
    [InlineData("rank", "eq", "7")]
    [InlineData("big", "eq", "9007199254740993")]
    public async Task ANumericValue_OnANumericField_Passes(string attribute, string op, string value)
    {
        var result = await Check($"<condition attribute=\"{attribute}\" operator=\"{op}\" value=\"{value}\" />");

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
    }

    /// <summary>
    /// Verifies that a non numeric value on a numeric field is rejected and the model is told why.
    /// </summary>
    [Theory]
    [InlineData("numberofemployees", "eq", "account")]
    [InlineData("numberofemployees", "gt", "many")]
    [InlineData("revenue", "ge", "1,5")]
    [InlineData("score", "lt", "")]
    [InlineData("big", "eq", "12abc")]
    public async Task ANonNumericValue_OnANumericField_IsRejected_AndTheModelIsToldWhy(string attribute, string op, string value)
    {
        var result = await Check($"<condition attribute=\"{attribute}\" operator=\"{op}\" value=\"{value}\" />");

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains(attribute) && e.Contains("number"));
    }

    /// <summary>
    /// Verifies that every value of an in list is checked.
    /// </summary>
    [Fact]
    public async Task EveryValueOfAnInList_IsChecked()
    {
        var result = await Check("<condition attribute=\"numberofemployees\" operator=\"in\"><value>10</value><value>twenty</value></condition>");

        Assert.False(result.IsValid);
    }

    /// <summary>
    /// Verifies that between and null operators are handled.
    /// </summary>
    [Fact]
    public async Task BetweenAndNullOperators_AreHandled()
    {
        var between = await Check("<condition attribute=\"numberofemployees\" operator=\"between\"><value>10</value><value>20</value></condition>");
        var nul = await Check("<condition attribute=\"numberofemployees\" operator=\"null\" />");
        var badBetween = await Check("<condition attribute=\"numberofemployees\" operator=\"between\"><value>10</value><value>x</value></condition>");

        Assert.True(between.IsValid, string.Join("; ", between.Errors));
        Assert.True(nul.IsValid, string.Join("; ", nul.Errors));
        Assert.False(badBetween.IsValid);
    }

    /// <summary>
    /// Verifies that the hidden type companion of a lookup cannot be filtered by name.
    /// </summary>
    [Theory]
    [InlineData("customeridtype", "account")]
    [InlineData("owneridtype", "systemuser")]
    [InlineData("parentcustomeridtype", "account")]
    public async Task TheHiddenTypeCompanionOfALookup_CannotBeFilteredByName(string attribute, string value)
    {
        var entity = new EntityInfo("opportunity", "opportunities", "Opportunity", "opportunityid", "name",
            new List<AttributeInfo> { A("opportunityid", "Uniqueidentifier"), A("name", "String"), A(attribute, "EntityName") }, new List<RelationshipInfo>());
        var xml = "<fetch><entity name=\"opportunity\"><attribute name=\"name\" /><filter><condition attribute=\"" + attribute + "\" operator=\"eq\" value=\"" + value + "\" /></filter></entity></fetch>";

        var result = await new PlanValidator(new FakeMetadataSource(entity)).ValidateFetchXmlAsync(xml);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains(attribute) && e.Contains("link-entity"));
    }

    /// <summary>
    /// Verifies that an entity name attribute that is not a lookup companion is not affected.
    /// </summary>
    [Fact]
    public async Task AnEntityNameAttribute_ThatIsNotALookupCompanion_IsNotAffected()
    {
        var entity = new EntityInfo("annotation", "annotations", "Note", "annotationid", "subject",
            new List<AttributeInfo> { A("annotationid", "Uniqueidentifier"), A("subject", "String"), A("objecttypecode", "EntityName") }, new List<RelationshipInfo>());
        var xml = "<fetch><entity name=\"annotation\"><attribute name=\"subject\" /><filter><condition attribute=\"objecttypecode\" operator=\"eq\" value=\"account\" /></filter></entity></fetch>";

        var result = await new PlanValidator(new FakeMetadataSource(entity)).ValidateFetchXmlAsync(xml);

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
    }

    /// <summary>
    /// Verifies that other field types are not affected.
    /// </summary>
    [Fact]
    public async Task OtherFieldTypes_AreNotAffected()
    {
        var result = await Check("<condition attribute=\"name\" operator=\"eq\" value=\"1000 Cranes Ltd\" />");

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
    }
}
