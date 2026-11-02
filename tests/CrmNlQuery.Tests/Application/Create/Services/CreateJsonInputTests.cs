using CrmNlQuery.Application.Create.Models;
using CrmNlQuery.Application.Create.Services;
using CrmNlQuery.Domain.Create.Entities;

namespace CrmNlQuery.Tests.Application.Create.Services;

/// <summary>
/// Unit tests for parsing pasted JSON into a record creation request without any model.
/// </summary>
public class CreateJsonInputTests
{
    private static readonly IReadOnlyList<CreateSchema> Writable = new[] { CreateValidatorTests.AccountSchema() };

    private static CreateJson Parse(string text)
    {
        Assert.Equal(CreateJsonResult.Ok, CreateJsonInput.TryParse(text, Writable, out var json));
        return json!;
    }

    /// <summary>
    /// Verifies that a plain object is one record.
    /// </summary>
    [Fact]
    public void APlainObject_IsOneRecord()
    {
        var json = Parse("hesap oluştur {\"name\": \"Alpine\", \"numberofemployees\": 40, \"donotphone\": true, \"telephone1\": null}");

        Assert.Null(json.Entity);
        var record = Assert.Single(json.Records);
        Assert.Equal("Alpine", record["name"]);
        Assert.Equal("40", record["numberofemployees"]);
        Assert.Equal("true", record["donotphone"]);
        Assert.False(record.ContainsKey("telephone1"));
    }

    /// <summary>
    /// Verifies that an array is several records.
    /// </summary>
    [Fact]
    public void AnArray_IsSeveralRecords()
    {
        var json = Parse("[{\"name\":\"A\"},{\"name\":\"B\"}]");

        Assert.Equal(2, json.Records.Count);
    }

    /// <summary>
    /// Verifies that the table can come from the JSON itself.
    /// </summary>
    [Theory]
    [InlineData("{\"entity\":\"account\",\"records\":[{\"name\":\"A\"}]}")]
    [InlineData("{\"accounts\":[{\"name\":\"A\"}]}")]
    [InlineData("{\"account\":{\"name\":\"A\"}}")]
    public void TheTableCanComeFromTheJsonItself(string text)
    {
        var json = Parse(text);

        Assert.Single(json.Records);
        Assert.Equal("A", json.Records[0]["name"]);
        Assert.True(json.Entity is "account" or "Account", "entity was " + json.Entity);
    }

    /// <summary>
    /// Verifies that JSON inside a fenced block is found.
    /// </summary>
    [Fact]
    public void JsonInsideAFencedBlock_IsFound()
    {
        var json = Parse("Şunu ekle:\n```json\n{\"name\": \"Alpine\"}\n```\nteşekkürler");

        Assert.Equal("Alpine", Assert.Single(json.Records)["name"]);
    }

    /// <summary>
    /// Verifies that nested values and non objects are not supported.
    /// </summary>
    [Theory]
    [InlineData("{\"name\": {\"first\": \"A\"}}")]
    [InlineData("[{\"name\": [\"A\"]}]")]
    [InlineData("[1,2,3]")]
    public void NestedValuesAndNonObjects_AreNotSupported(string text)
    {
        Assert.Equal(CreateJsonResult.Unsupported, CreateJsonInput.TryParse(text, Writable, out _));
    }

    /// <summary>
    /// Verifies that plain text is not JSON.
    /// </summary>
    [Theory]
    [InlineData("hesap oluştur Alpine")]
    [InlineData("merhaba { bu yarım")]
    public void PlainText_IsNotJson(string text)
    {
        Assert.Equal(CreateJsonResult.NotJson, CreateJsonInput.TryParse(text, Writable, out _));
    }
}
