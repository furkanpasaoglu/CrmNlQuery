using CrmNlQuery.Application.Ask.Planning.Validation;
using CrmNlQuery.Tests.Support;
using CrmNlQuery.Domain.Metadata.Entities;

namespace CrmNlQuery.Tests.Application.Ask.Planning.Validation;

/// <summary>
/// Tests validation of multi-select and yes/no field values in queries.
/// </summary>
public class OptionValueValidationTests
{
    private static EntityInfo Vehicle() => new EntityInfo("new_arac", "new_aracs", "Araç", "new_aracid", "new_plaka",
        new List<AttributeInfo>
        {
            new AttributeInfo("new_aracid", "Id", "Uniqueidentifier", new List<OptionInfo>()),
            new AttributeInfo("new_plaka", "Plaka", "String", new List<OptionInfo>()),
            new AttributeInfo("new_yakit", "Yakıt", "Picklist", new List<OptionInfo> { new OptionInfo(100000000, "Benzin"), new OptionInfo(100000001, "Dizel") }),
            new AttributeInfo("new_garantili", "Garantili", "Boolean", new List<OptionInfo> { new OptionInfo(1, "Evet"), new OptionInfo(0, "Hayır") }),
            new AttributeInfo("new_ozellikler", "Özellikler", "MultiSelectPicklist", new List<OptionInfo> { new OptionInfo(100000000, "Sunroof"), new OptionInfo(100000001, "Deri koltuk") }),
        },
        new List<RelationshipInfo>());

    private static async Task<ValidationResult> Validate(string condition)
    {
        var validator = new PlanValidator(new FakeMetadataSource(Vehicle()));
        var xml = $"<fetch top=\"500\"><entity name=\"new_arac\"><attribute name=\"new_plaka\" /><filter>{condition}</filter></entity></fetch>";
        return await validator.ValidateFetchXmlAsync(xml, null);
    }

    /// <summary>
    /// Verifies that contain-values accepts valid multi-select option values.
    /// </summary>
    [Theory]
    [InlineData("<condition attribute=\"new_ozellikler\" operator=\"contain-values\"><value>100000000</value></condition>")]
    [InlineData("<condition attribute=\"new_ozellikler\" operator=\"not-contain-values\"><value>100000001</value></condition>")]
    [InlineData("<condition attribute=\"new_ozellikler\" operator=\"contain-values\"><value>100000000</value><value>100000001</value></condition>")]
    [InlineData("<condition attribute=\"new_ozellikler\" operator=\"null\" />")]
    [InlineData("<condition attribute=\"new_ozellikler\" operator=\"not-null\" />")]
    public async Task MultiSelect_AcceptsContainValues_WithListedOptions_AndNullChecks(string condition)
    {
        var result = await Validate(condition);

        Assert.True(result.IsValid, string.Join(" | ", result.Errors));
    }

    /// <summary>
    /// Verifies that multi select rejects a value that is not an option.
    /// </summary>
    [Fact]
    public async Task MultiSelect_RejectsAValueThatIsNotAnOption()
    {
        var result = await Validate("<condition attribute=\"new_ozellikler\" operator=\"contain-values\"><value>5</value></condition>");

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("100000000=Sunroof"));
    }

    /// <summary>
    /// Verifies that multi-select rejects other operators such as eq, in and like.
    /// </summary>
    [Theory]
    [InlineData("eq")]
    [InlineData("in")]
    [InlineData("like")]
    public async Task MultiSelect_RejectsTheOrdinaryOperators(string op)
    {
        var result = await Validate($"<condition attribute=\"new_ozellikler\" operator=\"{op}\"><value>100000000</value></condition>");

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("contain-values"));
    }

    /// <summary>
    /// Verifies that contain-values is rejected on an ordinary option set.
    /// </summary>
    [Fact]
    public async Task ContainValues_IsRejected_OnAnOrdinaryPicklist()
    {
        var result = await Validate("<condition attribute=\"new_yakit\" operator=\"contain-values\"><value>100000001</value></condition>");

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("multi-select"));
    }

    /// <summary>
    /// Verifies that yes/no fields accept the valid boolean values.
    /// </summary>
    [Theory]
    [InlineData("1", true)]
    [InlineData("0", true)]
    [InlineData("true", true)]
    [InlineData("False", true)]
    [InlineData("2", false)]
    [InlineData("yes", false)]
    public async Task Boolean_AcceptsOneZeroTrueFalse_AndNothingElse(string value, bool valid)
    {
        var result = await Validate($"<condition attribute=\"new_garantili\" operator=\"eq\"><value>{value}</value></condition>");

        Assert.Equal(valid, result.IsValid);
        if (!valid)
        {
            Assert.Contains(result.Errors, e => e.Contains("1=Evet"));
        }
    }
}
