using System.Text.Json;
using CrmNlQuery.Infrastructure.External.Crm;

namespace CrmNlQuery.Tests.Infrastructure.External.Crm;

/// <summary>
/// Tests how role and team names from the CRM become group names and that the service read of them stays narrow.
/// </summary>
public class CrmUserDirectoryTests
{
    private static JsonElement Rows(params string[] names)
        => JsonDocument.Parse("{\"value\":[" + string.Join(",", names.Select(n => "{\"name\":" + JsonSerializer.Serialize(n) + "}")) + "]}").RootElement;

    [Fact]
    public void NamesGetThePrefix_AreTrimmedAndDeduplicated()
    {
        var names = CrmUserDirectory.ParseNames(Rows("Sales Manager", "  Sales Manager ", "sales manager", "CRM Lab"), CrmUserDirectory.RolePrefix);

        Assert.Equal(new[] { "role:Sales Manager", "role:CRM Lab" }, names);
    }

    [Fact]
    public void ControlCharactersLongNamesAndEmptyNamesAreHandled()
    {
        var longName = new string('x', 300);

        var names = CrmUserDirectory.ParseNames(Rows("Bad\nName\t", "   ", longName), CrmUserDirectory.TeamPrefix);

        Assert.Equal("team:BadName", names[0]);
        Assert.Equal("team:".Length + 100, names[1].Length);
        Assert.Equal(2, names.Count);
    }

    [Fact]
    public void AMissingOrMalformedAnswerGivesNoGroups()
    {
        Assert.Empty(CrmUserDirectory.ParseNames(JsonDocument.Parse("{}").RootElement, "role:"));
        Assert.Empty(CrmUserDirectory.ParseNames(JsonDocument.Parse("{\"value\":\"x\"}").RootElement, "role:"));
        Assert.Empty(CrmUserDirectory.ParseNames(JsonDocument.Parse("{\"value\":[{\"other\":1},{\"name\":5}]}").RootElement, "role:"));
    }

    [Fact]
    public void AtMostAHundredGroupsAreKept()
    {
        var many = Enumerable.Range(0, 250).Select(i => "Role " + i).ToArray();

        Assert.Equal(100, CrmUserDirectory.ParseNames(Rows(many), "role:").Count);
    }

    [Theory]
    [InlineData("systemusers(11111111-1111-1111-1111-111111111111)/systemuserroles_association?$select=name&$top=200")]
    [InlineData("systemusers(AAAAAAAA-aaaa-aaaa-aaaa-111111111111)/teammembership_association?$select=name&$top=50")]
    public void TheRoleAndTeamNameReadsOfOneUserAreAllowed(string url)
    {
        ServiceReadPolicy.EnsureAllowed(url);
    }

    [Theory]
    [InlineData("systemusers(11111111-1111-1111-1111-111111111111)/systemuserroles_association")]
    [InlineData("systemusers(11111111-1111-1111-1111-111111111111)/systemuserroles_association?$select=name")]
    [InlineData("systemusers(11111111-1111-1111-1111-111111111111)/systemuserroles_association?$select=roleid,name&$top=10")]
    [InlineData("systemusers(11111111-1111-1111-1111-111111111111)/systemuserroles_association?$select=name&$top=500")]
    [InlineData("systemusers(11111111-1111-1111-1111-111111111111)/systemuserroles_association?$select=name&$top=10&$filter=name%20eq%20%27x%27")]
    [InlineData("systemusers(11111111-1111-1111-1111-111111111111)/systemuserroles_association?$select=name&$top=10&$expand=privileges")]
    [InlineData("systemusers(not-a-guid)/systemuserroles_association?$select=name&$top=10")]
    [InlineData("systemusers(11111111-1111-1111-1111-111111111111)/lk_accountbase_createdby?$select=name&$top=10")]
    [InlineData("systemusers(11111111-1111-1111-1111-111111111111)?$select=systemuserid")]
    [InlineData("roles?$select=name&$top=10")]
    [InlineData("teams?$select=name&$top=10")]
    public void AnythingBroaderIsRefused(string url)
    {
        Assert.Throws<InvalidOperationException>(() => ServiceReadPolicy.EnsureAllowed(url));
    }
}
