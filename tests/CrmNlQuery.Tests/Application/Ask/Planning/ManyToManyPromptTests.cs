using CrmNlQuery.Application.Ask.Planning;
using CrmNlQuery.Application.Ask.Planning.Prompts;
using CrmNlQuery.Application.Ask.Planning.Validation;
using CrmNlQuery.Tests.Support;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Domain.Metadata.Entities;

namespace CrmNlQuery.Tests.Application.Ask.Planning;

/// <summary>
/// Tests that many-to-many relationships appear in the planner prompt only when both ends are in the request.
/// </summary>
public class ManyToManyPromptTests
{
    private const string GoodPlan = "{\"steps\":[{\"fetchXml\":\"<fetch><entity name=\\\"new_arac\\\"><attribute name=\\\"new_aracid\\\" /><attribute name=\\\"new_plaka\\\" /></entity></fetch>\"}]}";

    private static AttributeInfo A(string name, string type = "String") => new AttributeInfo(name, name, type, new List<OptionInfo>());

    private static readonly ManyToManyInfo Link = new ManyToManyInfo("new_arac_aksesuar", "new_arac_aksesuar", "new_arac", "new_aracid", "new_aksesuar", "new_aksesuarid");

    private static EntityInfo Vehicle() => new EntityInfo("new_arac", "new_aracs", "Araç", "new_aracid", "new_plaka",
        new List<AttributeInfo> { A("new_aracid", "Uniqueidentifier"), A("new_plaka") }, new List<RelationshipInfo>()) { ManyToMany = new[] { Link } };

    private static EntityInfo Accessory() => new EntityInfo("new_aksesuar", "new_aksesuars", "Aksesuar", "new_aksesuarid", "new_name",
        new List<AttributeInfo> { A("new_aksesuarid", "Uniqueidentifier"), A("new_name") }, new List<RelationshipInfo>()) { ManyToMany = new[] { Link } };

    private static EntityInfo Intersect() => new EntityInfo("new_arac_aksesuar", "new_arac_aksesuarlar", "arac_aksesuar", "new_arac_aksesuarid", "",
        new List<AttributeInfo> { A("new_arac_aksesuarid", "Uniqueidentifier"), A("new_aracid", "Uniqueidentifier"), A("new_aksesuarid", "Uniqueidentifier") }, new List<RelationshipInfo>());

    private static PlanRequest Request(IReadOnlyList<EntityInfo> entities, IReadOnlyList<ResolvedRecord>? resolved = null)
        => new PlanRequest("q", entities, resolved ?? new List<ResolvedRecord>(), new List<string> { "new_plaka" }, false);

    private static async Task<string> UserPrompt(PlanRequest request)
    {
        var llm = new FakeLlm(GoodPlan);
        var generator = new LlmPlanGenerator(llm, new PlanValidator(new FakeMetadataSource(Vehicle(), Accessory(), Intersect())), pruneContext: false);
        await generator.GenerateAsync(request);
        return llm.Calls[0].User;
    }

    /// <summary>
    /// Verifies that when both ends are in the request the link is shown only once.
    /// </summary>
    [Fact]
    public async Task WhenBothEndsAreInTheRequest_TheLinkIsShown_OnlyOnce()
    {
        var user = await UserPrompt(Request(new[] { Vehicle(), Accessory() }));

        Assert.Single(System.Text.RegularExpressions.Regex.Matches(user, "new_arac <-> new_aksesuar"));
        Assert.Contains("new_arac <-> new_aksesuar via new_arac_aksesuar", user);
        Assert.Contains("new_arac_aksesuar.new_aracid = new_arac.new_aracid", user);
        Assert.Contains("new_arac_aksesuar.new_aksesuarid = new_aksesuar.new_aksesuarid", user);
    }

    /// <summary>
    /// Verifies that when the other end is not in the request the link is hidden and so is the note.
    /// </summary>
    [Fact]
    public async Task WhenTheOtherEndIsNotInTheRequest_TheLinkIsHidden_AndSoIsTheNote()
    {
        var user = await UserPrompt(Request(new[] { Vehicle() }));

        Assert.DoesNotContain("many-to-many", user);
        Assert.DoesNotContain("new_arac_aksesuar", user.Replace("Entity new_arac_aksesuar", ""));
    }

    /// <summary>
    /// Verifies that a record resolved from the other end counts as present.
    /// </summary>
    [Fact]
    public async Task ARecordResolvedFromTheOtherEnd_CountsAsPresent()
    {
        var resolved = new List<ResolvedRecord> { new ResolvedRecord("34 TST 001", "new_arac", Guid.Parse("11111111-1111-1111-1111-111111111111"), "34 TST 001") };

        var user = await UserPrompt(Request(new[] { Accessory() }, resolved));

        Assert.Contains("new_arac <-> new_aksesuar via new_arac_aksesuar", user);
    }

    /// <summary>
    /// Verifies that the note explains reaching the other entity through the intersect entity.
    /// </summary>
    [Fact]
    public async Task TheNote_ExplainsReachingTheOtherEntityThroughTheIntersectEntity()
    {
        var user = await UserPrompt(Request(new[] { Vehicle(), Accessory() }));

        Assert.Contains("through its intersect entity", user);
        Assert.Contains("<link-entity name=\"intersect\"", user);
    }

    /// <summary>
    /// Verifies that the examples include an intersect link for either root and they validate.
    /// </summary>
    [Fact]
    public async Task TheExamples_IncludeAnIntersectLink_ForEitherRoot_AndTheyValidate()
    {
        foreach (var entities in new[] { new[] { Vehicle(), Accessory() }, new[] { Accessory(), Vehicle() } })
        {
            var examples = PlanExamples.Build(entities);

            var link = examples.Single(e => e.Contains("link-entity name=\"new_arac_aksesuar\""));
            Assert.Contains($"<fetch top=\"500\"><entity name=\"{entities[0].LogicalName}\">", link);
            var validator = new PlanValidator(new FakeMetadataSource(Vehicle(), Accessory(), Intersect()));
            foreach (var xml in examples)
            {
                var result = await validator.ValidateFetchXmlAsync(xml);
                Assert.True(result.IsValid, xml + " => " + string.Join("; ", result.Errors));
            }
        }
    }

    /// <summary>
    /// Verifies that without any many to many the examples are exactly as before.
    /// </summary>
    [Fact]
    public void WithoutAnyManyToMany_TheExamplesAreExactlyAsBefore()
    {
        var plainVehicle = Vehicle() with { ManyToMany = Array.Empty<ManyToManyInfo>() };

        var examples = PlanExamples.Build(new[] { plainVehicle });

        Assert.Equal(2, examples.Count);
        Assert.DoesNotContain(examples, e => e.Contains("intersect"));
    }
}
