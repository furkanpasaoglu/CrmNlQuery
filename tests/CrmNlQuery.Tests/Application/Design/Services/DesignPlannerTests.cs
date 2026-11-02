using System.Text.Json;
using CrmNlQuery.Tests.Support;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.Design.Models;
using CrmNlQuery.Application.Design.Services;

namespace CrmNlQuery.Tests.Application.Design.Services;

/// <summary>
/// Unit tests for the design planner: concept extraction, schema search, reuse decisions, validation retries and fallback behavior with a fake model.
/// </summary>
public class DesignPlannerTests
{
    private const string Extraction = """{"language":"tr","summary":"Bayilerin servis randevularını takip etmek.","concepts":[{"term":"servis randevusu","synonyms":["servis kaydı","randevu","appointment"],"kind":"table"},{"term":"araç","synonyms":["vehicle"],"kind":"table"}]}""";
    private const string Decisions = """{"decisions":[{"entity":"new_servis_kaydi","action":"extend","reason":"Zaten servis kaydı tutuyor."},{"entity":"new_arac","action":"reuse","reason":"Araç tablosu hazır."}]}""";

    private const string GoodDesign = """
    {"summary":"Randevu tablosu ve servis kaydı genişletmesi.",
     "tables":[
      {"logicalName":"new_randevu","displayName":"Randevu","pluralName":"Randevular","ownership":"user","description":"Servis randevusu","extends":"","primaryNameLogical":"new_ad","primaryNameDisplay":"Ad",
       "fields":[{"logicalName":"new_tarih","displayName":"Tarih","type":"datetime","required":true},
                 {"logicalName":"new_servisid","displayName":"Servis kaydı","type":"lookup","required":false,"target":"new_servis_kaydi"},
                 {"logicalName":"new_durum","displayName":"Durum","type":"picklist","required":true,"options":[{"value":1,"label":"Planlandı"},{"value":2,"label":"İptal"}]}]},
      {"logicalName":"new_servis_kaydi","displayName":"Servis Kaydı","ownership":"user","extends":"new_servis_kaydi","fields":[{"logicalName":"new_not","displayName":"Not","type":"memo","required":false}]}],
     "relationships":[{"kind":"one-to-many","from":"new_servis_kaydi","to":"new_randevu","lookupField":"new_servisid","deleteBehavior":"restrict","description":"Bir kayda çok randevu"}],
     "views":[{"table":"new_randevu","name":"Aktif randevular","columns":["new_ad","new_tarih","new_durum"]}],
     "securityNotes":["Plaka kişisel veri sayılabilir."],
     "openQuestions":["İptal edilen randevular silinsin mi?","Aynı araç için çakışan randevuya izin var mı?"]}
    """;

    private sealed class ScriptedLlm : ILlmClient
    {
        public List<string> Designs { get; } = new List<string>();
        public List<string> ExtractionReplies { get; } = new List<string>();
        public string DecisionReply { get; set; } = Decisions;
        public int ExtractCalls { get; private set; }
        public int DecideCalls { get; private set; }
        public int DesignCalls { get; private set; }
        public List<string> DesignPrompts { get; } = new List<string>();

        /// <summary>
        /// Test double that complete json with canned data instead of calling a real dependency.
        /// </summary>
        public Task<string> CompleteJsonAsync(string system, string user, string jsonSchema, CancellationToken ct = default)
        {
            if (jsonSchema.Contains("\"concepts\""))
            {
                ExtractCalls++;
                return Task.FromResult(ExtractionReplies.Count >= ExtractCalls ? ExtractionReplies[ExtractCalls - 1] : Extraction);
            }
            if (jsonSchema.Contains("\"decisions\""))
            {
                DecideCalls++;
                return Task.FromResult(DecisionReply);
            }
            DesignCalls++;
            DesignPrompts.Add(user);
            return Task.FromResult(Designs.Count >= DesignCalls ? Designs[DesignCalls - 1] : GoodDesign);
        }
    }

    private static DesignPlanner Planner(ScriptedLlm llm, Func<string, bool>? closed = null)
        => new DesignPlanner(llm, new FakeMetadataSource(DesignTestData.All.ToArray()), closed is null ? null : _ => Task.FromResult(closed));

    private static readonly DesignRequest Request = new DesignRequest("Bayilerimizin servis randevularını CRM'de takip etmek istiyoruz.");

    /// <summary>
    /// Verifies that the full flow finds the existing tables keeps the decisions and returns a clean design.
    /// </summary>
    [Fact]
    public async Task TheFullFlow_FindsTheExistingTables_KeepsTheDecisions_AndReturnsACleanDesign()
    {
        var llm = new ScriptedLlm();

        var result = await Planner(llm).RunAsync(Request);

        Assert.Empty(result.Problems);
        Assert.Equal("new", result.Prefix);
        Assert.Equal("tr", result.Language);
        Assert.Equal("new_servis_kaydi", result.Matches[0].Entity);
        Assert.Equal(2, result.Decisions.Count);
        Assert.Equal("extend", result.Decisions.First(d => d.Entity == "new_servis_kaydi").Action);
        Assert.Equal(2, result.Document.Tables.Count);
        Assert.Equal((1, 1, 1), (llm.ExtractCalls, llm.DecideCalls, llm.DesignCalls));
    }

    /// <summary>
    /// Verifies that the design prompt carries the decisions and the existing fields but not the whole schema.
    /// </summary>
    [Fact]
    public async Task TheDesignPrompt_CarriesTheDecisionsAndTheExistingFields_ButNotTheWholeSchema()
    {
        var llm = new ScriptedLlm();

        await Planner(llm).RunAsync(Request);

        var prompt = llm.DesignPrompts[0];
        Assert.Contains("new_servis_kaydi", prompt);
        Assert.Contains("\"action\": \"extend\"", prompt);
        Assert.Contains("new_tutar", prompt);
        Assert.Contains("Publisher prefix: new", prompt);
        Assert.DoesNotContain("Case Title", prompt);
    }

    /// <summary>
    /// Verifies that a design with problems is sent back to the model with the errors and the fixed one is used.
    /// </summary>
    [Fact]
    public async Task ADesignWithProblems_IsSentBackToTheModelWithTheErrors_AndTheFixedOneIsUsed()
    {
        var llm = new ScriptedLlm();
        llm.Designs.Add(GoodDesign.Replace("\"new_randevu\",\"displayName\"", "\"randevu\",\"displayName\""));

        var result = await Planner(llm).RunAsync(Request);

        Assert.Equal(2, llm.DesignCalls);
        Assert.Contains("must start with the publisher prefix", llm.DesignPrompts[1]);
        Assert.Empty(result.Problems);
    }

    /// <summary>
    /// Verifies that an extend decision whose table is written without extends is fixed in code without a retry.
    /// </summary>
    [Fact]
    public async Task AnExtendDecision_WhoseTableIsWrittenWithoutExtends_IsFixedInCode_WithoutARetry()
    {
        var llm = new ScriptedLlm();
        llm.Designs.Add(GoodDesign.Replace("\"extends\":\"new_servis_kaydi\"", "\"extends\":\"\""));

        var result = await Planner(llm).RunAsync(Request);

        Assert.Equal(1, llm.DesignCalls);
        Assert.Empty(result.Problems);
        Assert.Equal("new_servis_kaydi", result.Document.Tables.First(t => t.LogicalName == "new_servis_kaydi").Extends);
    }

    /// <summary>
    /// Verifies that an existing table without an extend decision is not silently turned into an extension.
    /// </summary>
    [Fact]
    public async Task AnExistingTableWithoutAnExtendDecision_IsNotSilentlyTurnedIntoAnExtension()
    {
        var llm = new ScriptedLlm { DecisionReply = """{"decisions":[{"entity":"new_servis_kaydi","action":"ignore","reason":"x"},{"entity":"new_arac","action":"reuse","reason":"y"}]}""" };
        llm.Designs.AddRange(new[] { GoodDesign.Replace("\"extends\":\"new_servis_kaydi\"", "\"extends\":\"\""), GoodDesign.Replace("\"extends\":\"new_servis_kaydi\"", "\"extends\":\"\""), GoodDesign.Replace("\"extends\":\"new_servis_kaydi\"", "\"extends\":\"\"") });

        var result = await Planner(llm).RunAsync(Request);

        Assert.NotEmpty(result.Problems);
    }

    /// <summary>
    /// Verifies that when no design is ever valid the least bad one is returned with its problems.
    /// </summary>
    [Fact]
    public async Task WhenNoDesignIsEverValid_TheLeastBadOneIsReturned_WithItsProblems()
    {
        var llm = new ScriptedLlm();
        var bad = GoodDesign.Replace("\"new_randevu\",\"displayName\"", "\"randevu\",\"displayName\"");
        llm.Designs.AddRange(new[] { bad, bad, bad });

        var result = await Planner(llm).RunAsync(Request);

        Assert.Equal(3, llm.DesignCalls);
        Assert.NotEmpty(result.Problems);
        Assert.NotEmpty(result.Document.Tables);
    }

    /// <summary>
    /// Verifies that a decision about a non candidate is dropped and the model is asked again.
    /// </summary>
    [Fact]
    public async Task ADecisionAboutANonCandidate_IsDropped_AndTheModelIsAskedAgain()
    {
        var llm = new ScriptedLlm { DecisionReply = """{"decisions":[{"entity":"new_arac","action":"reuse","reason":"x"},{"entity":"account","action":"extend","reason":"y"}]}""" };

        var result = await Planner(llm).RunAsync(Request);

        Assert.Equal(2, llm.DecideCalls);
        Assert.Equal(new[] { "new_arac" }, result.Decisions.Select(d => d.Entity));
    }

    /// <summary>
    /// Verifies that without any candidate no reuse question is asked.
    /// </summary>
    [Fact]
    public async Task WithoutAnyCandidate_NoReuseQuestionIsAsked()
    {
        var llm = new ScriptedLlm();
        llm.ExtractionReplies.Add("""{"language":"tr","summary":"s","concepts":[{"term":"eğitim katılımı","synonyms":["training"],"kind":"table"}]}""");

        var result = await Planner(llm).RunAsync(new DesignRequest("Bayi eğitimlerine katılımı takip etmek istiyoruz."));

        Assert.Equal(0, llm.DecideCalls);
        Assert.Empty(result.Matches);
        Assert.Empty(result.Decisions);
    }

    /// <summary>
    /// Verifies that when the model cannot extract concepts the request words become search terms.
    /// </summary>
    [Fact]
    public async Task WhenTheModelCannotExtractConcepts_TheRequestWordsBecomeSearchTerms()
    {
        var llm = new ScriptedLlm();
        llm.ExtractionReplies.AddRange(new[] { "not json", "still not json" });

        var result = await Planner(llm).RunAsync(new DesignRequest("Servis kaydı tablosunu genişletmek istiyoruz"));

        Assert.Equal(2, llm.ExtractCalls);
        Assert.Contains(result.Matches, m => m.Entity == "new_servis_kaydi");
    }

    /// <summary>
    /// Verifies that an explicit prefix wins over the detected one.
    /// </summary>
    [Fact]
    public async Task AnExplicitPrefix_WinsOverTheDetectedOne()
    {
        var llm = new ScriptedLlm();

        var result = await Planner(llm).RunAsync(new DesignRequest(Request.Text, "ACME"));

        Assert.Equal("acme", result.Prefix);
        Assert.NotEmpty(result.Problems);
    }

    /// <summary>
    /// Verifies that progress is reported in order and closed tables are noted.
    /// </summary>
    [Fact]
    public async Task ProgressIsReportedInOrder_AndClosedTablesAreNoted()
    {
        var steps = new List<string>();

        var result = await Planner(new ScriptedLlm(), name => name == "new_arac").RunAsync(Request, (step, percent) => steps.Add(step));

        Assert.Equal(new[] { "Understanding the request", "Searching the CRM schema", "Deciding what to reuse", "Designing", "Designing", "Finishing" }.Distinct(), steps.Distinct());
        Assert.Equal("Understanding the request", steps[0]);
        Assert.Equal("Finishing", steps[^1]);
        Assert.Equal(new[] { "new_arac" }, result.ClosedTables);
    }

    /// <summary>
    /// Verifies that the design JSON schema sent to the model is valid JSON without numeric bounds.
    /// </summary>
    [Fact]
    public async Task TheDesignJsonSchemaSentToTheModel_IsValidJson_WithoutNumericBounds()
    {
        string? seen = null;
        var llm = new CapturingSchemaLlm(s => seen = s);

        await new DesignPlanner(llm, new FakeMetadataSource(DesignTestData.All.ToArray())).RunAsync(Request);

        Assert.NotNull(seen);
        using var doc = JsonDocument.Parse(seen!);
        Assert.DoesNotContain("\"minimum\"", seen);
        Assert.DoesNotContain("\"maximum\"", seen);
    }

    private sealed class CapturingSchemaLlm : ILlmClient
    {
        private readonly Action<string> _onDesignSchema;
        public CapturingSchemaLlm(Action<string> onDesignSchema) { _onDesignSchema = onDesignSchema; }
        /// <summary>
        /// Test double that complete json with canned data instead of calling a real dependency.
        /// </summary>
        public Task<string> CompleteJsonAsync(string system, string user, string jsonSchema, CancellationToken ct = default)
        {
            if (jsonSchema.Contains("\"concepts\"")) { return Task.FromResult(Extraction); }
            if (jsonSchema.Contains("\"decisions\"")) { return Task.FromResult(Decisions); }
            _onDesignSchema(jsonSchema);
            return Task.FromResult(GoodDesign);
        }
    }
}
