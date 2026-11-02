using CrmNlQuery.Application.Create.Services;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Domain.Create.Entities;
using CrmNlQuery.Domain.Create.Enums;

namespace CrmNlQuery.Tests.Application.Create.Services;

/// <summary>
/// Tests the record creation validator: typed values, required fields, choices, lookups and problem reporting.
/// </summary>
public class CreateValidatorTests
{
    private static readonly Guid Acme = Guid.Parse("11111111-1111-1111-1111-111111111111");

    /// <summary>
    /// Test helper that builds an account creation schema.
    /// </summary>
    internal static CreateSchema AccountSchema() => new CreateSchema("account", "accounts", "Account", "accountid", "name", new[]
    {
        new CreateField("name", "Account Name", CreateFieldType.Text, true, 20, Array.Empty<CreateOption>(), Array.Empty<LookupTarget>()),
        new CreateField("telephone1", "Main Phone", CreateFieldType.Text, false, 50, Array.Empty<CreateOption>(), Array.Empty<LookupTarget>()),
        new CreateField("numberofemployees", "Number of Employees", CreateFieldType.WholeNumber, false, null, Array.Empty<CreateOption>(), Array.Empty<LookupTarget>()),
        new CreateField("revenue", "Annual Revenue", CreateFieldType.Decimal, false, null, Array.Empty<CreateOption>(), Array.Empty<LookupTarget>()),
        new CreateField("donotphone", "Do not allow Phone Calls", CreateFieldType.Boolean, false, null, Array.Empty<CreateOption>(), Array.Empty<LookupTarget>()),
        new CreateField("createdate", "Founded", CreateFieldType.Date, false, null, Array.Empty<CreateOption>(), Array.Empty<LookupTarget>()),
        new CreateField("industrycode", "Industry", CreateFieldType.Choice, false, null, new[] { new CreateOption(1, "Accounting"), new CreateOption(2, "Agriculture") }, Array.Empty<LookupTarget>()),
        new CreateField("parentaccountid", "Parent Account", CreateFieldType.Lookup, false, null, Array.Empty<CreateOption>(), new[] { new LookupTarget("account", "accounts", "parentaccountid") }),
    });

    private static CreateValidator Validator(params RecordMatch[] matches)
        => new CreateValidator((entity, text, ct) => Task.FromResult<IReadOnlyList<RecordMatch>>(matches.Where(m => m.Name.Contains(text, StringComparison.OrdinalIgnoreCase)).ToList()));

    private static Dictionary<string, string> Raw(params (string, string)[] pairs) => pairs.ToDictionary(p => p.Item1, p => p.Item2);

    /// <summary>
    /// Verifies that a valid record has typed values and a payload.
    /// </summary>
    [Fact]
    public async Task AValidRecord_HasTypedValues_AndAPayload()
    {
        var row = await Validator().ValidateAsync(AccountSchema(), Raw(("name", "Alpine"), ("numberofemployees", "1.250"), ("revenue", "1.234,50"), ("donotphone", "Evet"), ("createdate", "05.03.2020"), ("industrycode", "agriculture")));

        Assert.True(row.IsComplete);
        Assert.Equal("Alpine", row.DisplayName);
        var payload = row.ToPayload();
        Assert.Equal("Alpine", payload["name"]);
        Assert.Equal(1250L, payload["numberofemployees"]);
        Assert.Equal(1234.50m, payload["revenue"]);
        Assert.Equal(true, payload["donotphone"]);
        Assert.Equal("2020-03-05T00:00:00Z", payload["createdate"]);
        Assert.Equal(2, payload["industrycode"]);
    }

    /// <summary>
    /// Verifies that a missing required field is reported rather than invented.
    /// </summary>
    [Fact]
    public async Task AMissingRequiredField_IsReported_NotInvented()
    {
        var row = await Validator().ValidateAsync(AccountSchema(), Raw(("telephone1", "0555 000 00 00")));

        Assert.False(row.IsComplete);
        var problem = Assert.Single(row.Problems);
        Assert.Equal("missing_required", problem.Code);
        Assert.Equal("Account Name", problem.Label);
    }

    /// <summary>
    /// Verifies that fields are found by label ignoring case and punctuation.
    /// </summary>
    [Fact]
    public async Task Fields_AreFoundByLabel_CaseAndPunctuationInsensitive()
    {
        var row = await Validator().ValidateAsync(AccountSchema(), Raw(("ACCOUNT NAME", "Alpine"), ("main-phone", "123")));

        Assert.True(row.IsComplete);
        Assert.Equal("123", row.ToPayload()["telephone1"]);
    }

    /// <summary>
    /// Verifies that an unknown field is reported as a problem rather than silently dropped.
    /// </summary>
    [Fact]
    public async Task AnUnknownField_IsAProblem_NotSilentlyDropped()
    {
        var row = await Validator().ValidateAsync(AccountSchema(), Raw(("name", "Alpine"), ("ownerid", "someone")));

        Assert.Contains(row.Problems, p => p.Code == "unknown_field" && p.Attribute == "ownerid");
        Assert.False(row.IsComplete);
    }

    /// <summary>
    /// Verifies that invalid numeric values are reported with a specific problem code.
    /// </summary>
    [Theory]
    [InlineData("numberofemployees", "çok", "invalid_number")]
    [InlineData("revenue", "abc", "invalid_number")]
    [InlineData("donotphone", "belki", "invalid_boolean")]
    [InlineData("createdate", "yarın", "invalid_date")]
    [InlineData("industrycode", "Space", "choice_not_found")]
    public async Task WrongValues_AreReportedWithACode(string field, string value, string code)
    {
        var row = await Validator().ValidateAsync(AccountSchema(), Raw(("name", "Alpine"), (field, value)));

        Assert.Equal(code, Assert.Single(row.Problems).Code);
    }

    /// <summary>
    /// Verifies that an overly long text is rejected with the limit.
    /// </summary>
    [Fact]
    public async Task ATooLongText_IsRejected_WithTheLimit()
    {
        var row = await Validator().ValidateAsync(AccountSchema(), Raw(("name", new string('a', 21))));

        var problem = Assert.Single(row.Problems);
        Assert.Equal("too_long", problem.Code);
        Assert.Equal("20", problem.Detail);
    }

    /// <summary>
    /// Verifies that a choice problem lists the available options.
    /// </summary>
    [Fact]
    public async Task AChoiceProblem_ListsTheOptions()
    {
        var row = await Validator().ValidateAsync(AccountSchema(), Raw(("name", "A"), ("industrycode", "x")));

        Assert.Equal("Accounting, Agriculture", Assert.Single(row.Problems).Detail);
    }

    /// <summary>
    /// Verifies that a lookup is resolved by name into a binding.
    /// </summary>
    [Fact]
    public async Task ALookup_IsResolvedByName_ToABinding()
    {
        var row = await Validator(new RecordMatch(Acme, "Acme Corp")).ValidateAsync(AccountSchema(), Raw(("name", "Alpine"), ("parentaccountid", "Acme")));

        Assert.True(row.IsComplete);
        var payload = row.ToPayload();
        Assert.Equal("/accounts(11111111-1111-1111-1111-111111111111)", payload["parentaccountid@odata.bind"]);
        Assert.False(payload.ContainsKey("parentaccountid"));
    }

    /// <summary>
    /// Verifies that a lookup with no match or many matches is reported as a problem.
    /// </summary>
    [Fact]
    public async Task ALookup_WithNoMatch_OrManyMatches_IsAProblem()
    {
        var none = await Validator().ValidateAsync(AccountSchema(), Raw(("name", "A"), ("parentaccountid", "Nowhere")));
        var many = await Validator(new RecordMatch(Guid.NewGuid(), "Acme 1"), new RecordMatch(Guid.NewGuid(), "Acme 2")).ValidateAsync(AccountSchema(), Raw(("name", "A"), ("parentaccountid", "Acme")));

        Assert.Equal("lookup_not_found", Assert.Single(none.Problems).Code);
        var ambiguous = Assert.Single(many.Problems);
        Assert.Equal("lookup_ambiguous", ambiguous.Code);
        Assert.Contains("Acme 1", ambiguous.Detail);
    }

    /// <summary>
    /// Verifies that blank values are treated as missing.
    /// </summary>
    [Fact]
    public async Task BlankValues_AreTreatedAsMissing()
    {
        var row = await Validator().ValidateAsync(AccountSchema(), Raw(("name", "  ")));

        Assert.Equal("missing_required", Assert.Single(row.Problems).Code);
    }
}
