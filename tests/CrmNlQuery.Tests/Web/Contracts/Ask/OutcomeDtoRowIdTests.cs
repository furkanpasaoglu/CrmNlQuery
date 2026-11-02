using System.Text.Json;
using CrmNlQuery.Application.Ask.Models;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Web.Contracts.Ask;

namespace CrmNlQuery.Tests.Web.Contracts.Ask;

/// <summary>
/// Tests the row id fields of the outcome DTO.
/// </summary>
public class OutcomeDtoRowIdTests
{
    private static JsonElement Serialize(AskOutcome outcome)
    {
        var json = JsonSerializer.Serialize(OutcomeDto.From(outcome), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return JsonDocument.Parse(json).RootElement;
    }

    /// <summary>
    /// Verifies that row IDs and the record URL base are sent only when there are hidden IDs.
    /// </summary>
    [Fact]
    public void RowIdsAndTheRecordUrlBase_AreSent_OnlyWhenThereAreHiddenIds()
    {
        var rows = new List<IReadOnlyList<string>> { new List<string> { "A" } };
        var withIds = Serialize(new AnswerOutcome("account", "u", "<fetch/>", new ResultTable(new List<string> { "name" }, rows, new List<string> { "id-1" }), false)
        { RecordUrlBase = "http://crm/main.aspx?etn=account&pagetype=entityrecord&id=" });
        var without = Serialize(new AnswerOutcome("account", "u", "<fetch/>", new ResultTable(new List<string> { "name" }, rows), false)
        { RecordUrlBase = "http://crm/main.aspx?etn=account&pagetype=entityrecord&id=" });

        Assert.Equal("id-1", withIds.GetProperty("table").GetProperty("rowIds")[0].GetString());
        Assert.StartsWith("http://crm/main.aspx", withIds.GetProperty("recordUrlBase").GetString());
        Assert.Equal(JsonValueKind.Null, without.GetProperty("table").GetProperty("rowIds").ValueKind);
        Assert.Equal(JsonValueKind.Null, without.GetProperty("recordUrlBase").ValueKind);
    }
}
