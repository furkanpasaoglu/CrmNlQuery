using System.Text.Json;
using CrmNlQuery.Infrastructure.External.Crm;

namespace CrmNlQuery.Tests.Infrastructure.External.Crm;

/// <summary>
/// Tests how the CRM answer for the sign-in proof task is turned into a proof, especially that any sign of change after creation counts.
/// </summary>
public class CrmLoginProofReaderTests
{
    private const string Alice = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
    private const string Bob = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";

    private static JsonElement Rows(params string[] rows)
    {
        return JsonDocument.Parse("{\"value\":[" + string.Join(",", rows) + "]}").RootElement;
    }

    private static string Row(string createdOn = "2026-10-27T18:00:00Z", string modifiedOn = "2026-10-27T18:00:00Z", string createdBy = Alice, string modifiedBy = Alice,
        string? createdOnBehalf = null, string? modifiedOnBehalf = null)
    {
        string Field(string name, string? value) => value is null ? "\"" + name + "\":null" : "\"" + name + "\":\"" + value + "\"";
        return "{" + string.Join(",", new[]
        {
            Field("activityid", "11111111-1111-1111-1111-111111111111"),
            Field("createdon", createdOn), Field("modifiedon", modifiedOn),
            Field("_createdby_value", createdBy), Field("_modifiedby_value", modifiedBy),
            Field("_createdonbehalfby_value", createdOnBehalf), Field("_modifiedonbehalfby_value", modifiedOnBehalf),
        }) + "}";
    }

    [Fact]
    public void AnUntouchedRecordCreatedByTheUserIsAGoodProof()
    {
        var proof = CrmLoginProofReader.Parse(Rows(Row()));

        Assert.NotNull(proof);
        Assert.Equal(Guid.Parse(Alice), proof!.CreatedByUserId);
        Assert.False(proof.CreatedOnBehalf);
        Assert.False(proof.ModifiedAfterCreation);
    }

    [Fact]
    public void ARenamedRecordIsMarkedAsChanged_EvenThoughTheCreatorStays()
    {
        var proof = CrmLoginProofReader.Parse(Rows(Row(modifiedOn: "2026-10-27T18:05:00Z", modifiedBy: Bob)));

        Assert.True(proof!.ModifiedAfterCreation);
        Assert.Equal(Guid.Parse(Alice), proof.CreatedByUserId);
    }

    [Fact]
    public void ADifferentEditorOrAnEditOnBehalfIsMarkedAsChanged()
    {
        Assert.True(CrmLoginProofReader.Parse(Rows(Row(modifiedBy: Bob)))!.ModifiedAfterCreation);
        Assert.True(CrmLoginProofReader.Parse(Rows(Row(modifiedOnBehalf: Bob)))!.ModifiedAfterCreation);
    }

    [Fact]
    public void ACreationOnBehalfIsReported()
    {
        Assert.True(CrmLoginProofReader.Parse(Rows(Row(createdOnBehalf: Bob)))!.CreatedOnBehalf);
    }

    [Fact]
    public void MissingTimestampsCountAsChanged_SoTheProofFailsClosed()
    {
        var withoutTimes = "{\"activityid\":\"11111111-1111-1111-1111-111111111111\",\"_createdby_value\":\"" + Alice + "\"}";

        Assert.True(CrmLoginProofReader.Parse(Rows(withoutTimes))!.ModifiedAfterCreation);
    }

    [Fact]
    public void NoRowOrMoreThanOneRowMeansNoProof()
    {
        Assert.Null(CrmLoginProofReader.Parse(Rows()));
        Assert.Null(CrmLoginProofReader.Parse(Rows(Row(), Row())));
        Assert.Null(CrmLoginProofReader.Parse(JsonDocument.Parse("{}").RootElement));
    }
}
