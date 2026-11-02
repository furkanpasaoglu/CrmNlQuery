using System.Text.Json;
using CrmNlQuery.Application.Ask.Models;
using CrmNlQuery.Application.Explain.Models;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Web.Contracts.Ask;

namespace CrmNlQuery.Tests.Web.Contracts.Ask;

/// <summary>
/// Tests the serialization of server outcomes to the DTOs sent to the client.
/// </summary>
public class OutcomeDtoTests
{
    private static JsonElement Serialize(AskOutcome outcome)
    {
        var json = JsonSerializer.Serialize(OutcomeDto.From(outcome), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return JsonDocument.Parse(json).RootElement;
    }

    /// <summary>
    /// Verifies that a draft DTO carries the filled text, recipient and unresolved markers but not the template.
    /// </summary>
    [Fact]
    public void Draft_SerializesTheFilledTextTheRecipientAndTheUnresolvedMarkers_ButNotTheTemplate()
    {
        var draft = new DraftOutcome("account", "abc", "Brightwave", "Subject", "Body [your name]", "ayse@example.test", "formal", new List<string> { "[your name]" }, "T {{customer_name}}", "B {{sender_name}}")
        {
            ResultId = "r1",
            ChatId = "c1",
        };

        var json = Serialize(draft);

        Assert.Equal("draft", json.GetProperty("kind").GetString());
        Assert.Equal("Subject", json.GetProperty("subject").GetString());
        Assert.Equal("Body [your name]", json.GetProperty("body").GetString());
        Assert.Equal("ayse@example.test", json.GetProperty("to").GetString());
        Assert.Equal("[your name]", json.GetProperty("unresolved")[0].GetString());
        Assert.Equal("r1", json.GetProperty("resultId").GetString());
        Assert.False(json.TryGetProperty("templateSubject", out _));
        Assert.DoesNotContain("{{", json.ToString());
    }

    /// <summary>
    /// Verifies that an answer DTO serializes the table and flags.
    /// </summary>
    [Fact]
    public void Answer_SerializesTableAndFlags()
    {
        var table = new ResultTable(new List<string> { "name" }, new List<IReadOnlyList<string>> { new List<string> { "Chair" } });
        var json = Serialize(new AnswerOutcome("product", "Showing Product", "<fetch/>", table, true));
        Assert.Equal("answer", json.GetProperty("kind").GetString());
        Assert.True(json.GetProperty("truncated").GetBoolean());
        Assert.Equal("Chair", json.GetProperty("table").GetProperty("rows")[0][0].GetString());
    }

    private static ResultTable OneRow() => new ResultTable(new List<string> { "name" }, new List<IReadOnlyList<string>> { new List<string> { "Chair" } });

    /// <summary>
    /// Verifies that raw FetchXML is never sent unless the setting is on.
    /// </summary>
    [Fact]
    public void RawFetchXml_IsNeverSent_UnlessTheSettingIsOn()
    {
        var hidden = Serialize(new AnswerOutcome("product", "u", "<fetch/>", OneRow(), false));
        var shown = Serialize(new AnswerOutcome("product", "u", "<fetch/>", OneRow(), false) { ShowFetchXml = true });

        Assert.Equal(JsonValueKind.Null, hidden.GetProperty("fetchXml").ValueKind);
        Assert.DoesNotContain("<fetch", hidden.ToString());
        Assert.Equal("<fetch/>", shown.GetProperty("fetchXml").GetString());
    }

    /// <summary>
    /// Verifies that the explanation is serialized with labels and ranges, no GUIDs, and is null when there is none.
    /// </summary>
    [Fact]
    public void Explanation_IsSerialized_WithLabelsRangesAndNoGuids_AndNullWhenThereIsNone()
    {
        var explanation = new QueryExplanation("opportunity", "Opportunity",
            new List<ExplainFilter>
            {
                new ExplainFilter("f1", "statecode", "Status", "eq", new List<ExplainValue> { new ExplainValue("0", "Open") }),
                new ExplainFilter("f2", "createdon", "Created On", "last-x-days", new List<ExplainValue> { new ExplainValue("30", null) }) { From = "2026-09-22", To = "2026-10-22", Via = "Account", Group = "o1" },
            },
            new List<ExplainSort> { new ExplainSort("estimatedvalue", "Est. Revenue", true) }, 10, false, new List<string>());

        var json = Serialize(new AnswerOutcome("opportunity", "u", "<fetch/>", OneRow(), false) { Explanation = explanation });
        var none = Serialize(new AnswerOutcome("opportunity", "u", "<fetch/>", OneRow(), false));

        var e = json.GetProperty("explanation");
        Assert.Equal("Opportunity", e.GetProperty("entityLabel").GetString());
        Assert.Equal(10, e.GetProperty("top").GetInt32());
        Assert.False(e.GetProperty("aggregate").GetBoolean());
        var f1 = e.GetProperty("filters")[0];
        Assert.Equal("f1", f1.GetProperty("id").GetString());
        Assert.Equal("Status", f1.GetProperty("fieldLabel").GetString());
        Assert.Equal("Open", f1.GetProperty("values")[0].GetProperty("label").GetString());
        var f2 = e.GetProperty("filters")[1];
        Assert.Equal("2026-09-22", f2.GetProperty("from").GetString());
        Assert.Equal("Account", f2.GetProperty("via").GetString());
        Assert.Equal("o1", f2.GetProperty("group").GetString());
        Assert.True(e.GetProperty("sort")[0].GetProperty("descending").GetBoolean());
        Assert.Equal(JsonValueKind.Null, none.GetProperty("explanation").ValueKind);
    }

    /// <summary>
    /// Verifies that an answer DTO serializes step notes and they are empty by default.
    /// </summary>
    [Fact]
    public void Answer_SerializesStepNotes_EmptyByDefault()
    {
        var table = new ResultTable(new List<string> { "name" }, new List<IReadOnlyList<string>>());
        var plain = Serialize(new AnswerOutcome("product", "u", "<fetch/>", table, false));
        Assert.Equal(0, plain.GetProperty("stepNotes").GetArrayLength());

        var noted = Serialize(new AnswerOutcome("product", "u", "<fetch/>", table, false) { StepNotes = new List<string> { "Step 1 returned no rows." } });
        Assert.Equal("Step 1 returned no rows.", noted.GetProperty("stepNotes")[0].GetString());
    }

    /// <summary>
    /// Verifies that an answer DTO serializes the result id, null when there is none.
    /// </summary>
    [Fact]
    public void Answer_SerializesResultId_NullWhenThereIsNone()
    {
        var table = new ResultTable(new List<string> { "name" }, new List<IReadOnlyList<string>>());
        var plain = Serialize(new AnswerOutcome("product", "u", "<fetch/>", table, false));
        Assert.Equal(JsonValueKind.Null, plain.GetProperty("resultId").ValueKind);

        var withId = Serialize(new AnswerOutcome("product", "u", "<fetch/>", table, false) { ResultId = "abc123" });
        Assert.Equal("abc123", withId.GetProperty("resultId").GetString());
    }

    /// <summary>
    /// Verifies that a question DTO serializes choices and key.
    /// </summary>
    [Fact]
    public void Question_SerializesChoicesAndKey()
    {
        var q = new QuestionOutcome("columns", "columns", "Which?", new List<Choice> { new Choice("Default", "default") }, true);
        var json = Serialize(q);
        Assert.Equal("question", json.GetProperty("kind").GetString());
        Assert.Equal("columns", json.GetProperty("key").GetString());
        Assert.Equal("default", json.GetProperty("choices")[0].GetProperty("value").GetString());
        Assert.True(json.GetProperty("allowFreeText").GetBoolean());
    }

    /// <summary>
    /// Verifies that a failure DTO serializes the message.
    /// </summary>
    [Fact]
    public void Failure_SerializesMessage()
    {
        var json = Serialize(new FailureOutcome("boom"));
        Assert.Equal("failure", json.GetProperty("kind").GetString());
        Assert.Equal("boom", json.GetProperty("message").GetString());
    }

    /// <summary>
    /// Verifies that an answer carries the question it builds on only for a follow-up.
    /// </summary>
    [Fact]
    public void Answer_CarriesTheQuestionItBuildsOn_OnlyForAFollowUp()
    {
        var table = new ResultTable(new List<string> { "name" }, new List<IReadOnlyList<string>>());
        var follow = Serialize(new AnswerOutcome("account", "u", "<fetch/>", table, false) { BasedOn = "Hamburg'daki hesaplar" });
        var fresh = Serialize(new AnswerOutcome("account", "u", "<fetch/>", table, false));

        Assert.Equal("Hamburg'daki hesaplar", follow.GetProperty("basedOn").GetString());
        Assert.Equal(JsonValueKind.Null, fresh.GetProperty("basedOn").ValueKind);
    }

    /// <summary>
    /// Verifies that the ask dto accepts the chat ID with safe defaults and ignores the retired fields.
    /// </summary>
    [Fact]
    public void TheAskDto_AcceptsTheChatId_WithSafeDefaults_AndIgnoresTheRetiredFields()
    {
        var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var full = JsonSerializer.Deserialize<AskDto>("{\"question\":\"q\",\"chatId\":\"abc\",\"previousResultId\":\"old\",\"forceNew\":true}", web)!;
        var plain = JsonSerializer.Deserialize<AskDto>("{\"question\":\"q\"}", web)!;

        Assert.Equal("abc", full.ChatId);
        Assert.Null(plain.ChatId);
        Assert.Null(plain.Context);
    }

    /// <summary>
    /// Verifies that the answer dto carries the open in CRM flag false by default.
    /// </summary>
    [Fact]
    public void TheAnswerDto_CarriesTheOpenInCrmFlag_FalseByDefault()
    {
        var table = new ResultTable(new List<string> { "name" }, new List<IReadOnlyList<string>>());
        var plain = new CrmNlQuery.Application.Ask.Models.AnswerOutcome("account", "u", "<fetch />", table, false);

        var off = JsonSerializer.SerializeToElement(OutcomeDto.From(plain));
        var on = JsonSerializer.SerializeToElement(OutcomeDto.From(plain with { OpenInCrm = true }));

        Assert.False(off.GetProperty("openInCrm").GetBoolean());
        Assert.True(on.GetProperty("openInCrm").GetBoolean());
    }

    /// <summary>
    /// Verifies that the answer DTO carries narrowing hints as metadata only, empty when not truncated.
    /// </summary>
    [Fact]
    public void TheAnswerDto_CarriesTheNarrowHints_AsMetadataOnly_EmptyWhenNotTruncated()
    {
        var table = new ResultTable(new List<string> { "name" }, new List<IReadOnlyList<string>>());
        var plain = new CrmNlQuery.Application.Ask.Models.AnswerOutcome("opportunity", "u", "<fetch />", table, false);
        var hints = new List<NarrowHint>
        {
            new NarrowHint("statecode", "Status", "options", new List<string> { "Open", "Won", "Lost" }),
            new NarrowHint("address1_city", "City", "text", new List<string>()),
        };

        var none = JsonSerializer.SerializeToElement(OutcomeDto.From(plain));
        var some = JsonSerializer.SerializeToElement(OutcomeDto.From(plain with { NarrowHints = hints }));

        Assert.Equal(0, none.GetProperty("narrowHints").GetArrayLength());
        var first = some.GetProperty("narrowHints")[0];
        Assert.Equal("statecode", first.GetProperty("attribute").GetString());
        Assert.Equal("Status", first.GetProperty("label").GetString());
        Assert.Equal("options", first.GetProperty("kind").GetString());
        Assert.Equal(new[] { "Open", "Won", "Lost" }, first.GetProperty("options").EnumerateArray().Select(o => o.GetString()).ToArray());
        Assert.Equal("text", some.GetProperty("narrowHints")[1].GetProperty("kind").GetString());
    }
}
