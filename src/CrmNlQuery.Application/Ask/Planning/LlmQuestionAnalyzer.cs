using System.Text;
using System.Text.Json;
using CrmNlQuery.Application.Ask.Planning.Prompts;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Domain.Metadata.Entities;
using CrmNlQuery.Application.Common.Exceptions;

namespace CrmNlQuery.Application.Ask.Planning;

/// <summary>
/// Analyzes a question with the model into the primary entity, mentions and related entities.
/// </summary>
public sealed class LlmQuestionAnalyzer : IQuestionAnalyzer
{
    private const int MaxAttempts = 2;

    private const string Schema = """
    {"type":"object",
     "properties":{
       "primaryEntity":{"type":"string"},
       "relatedEntities":{"type":"array","items":{"type":"string"}},
       "mentions":{"type":"array","items":{"type":"object","properties":{"text":{"type":"string"},"entity":{"type":"string"}},"required":["text","entity"]}},
       "columns":{"type":"array","items":{"type":"string"}},
       "aggregate":{"type":"boolean"}},
     "required":["primaryEntity","relatedEntities","mentions","columns","aggregate"]}
    """;

    private const string SchemaWithPrevious = """
    {"type":"object",
     "properties":{
       "primaryEntity":{"type":"string"},
       "relatedEntities":{"type":"array","items":{"type":"string"}},
       "mentions":{"type":"array","items":{"type":"object","properties":{"text":{"type":"string"},"entity":{"type":"string"}},"required":["text","entity"]}},
       "columns":{"type":"array","items":{"type":"string"}},
       "aggregate":{"type":"boolean"},
       "referencesPrevious":{"type":"boolean"}},
     "required":["primaryEntity","relatedEntities","mentions","columns","aggregate","referencesPrevious"]}
    """;

    private const string SchemaWithContext = """
    {"type":"object",
     "properties":{
       "primaryEntity":{"type":"string"},
       "relatedEntities":{"type":"array","items":{"type":"string"}},
       "mentions":{"type":"array","items":{"type":"object","properties":{"text":{"type":"string"},"entity":{"type":"string"}},"required":["text","entity"]}},
       "columns":{"type":"array","items":{"type":"string"}},
       "aggregate":{"type":"boolean"},
       "referencesCurrentRecord":{"type":"boolean"}},
     "required":["primaryEntity","relatedEntities","mentions","columns","aggregate","referencesCurrentRecord"]}
    """;

    private const string SchemaWithBoth = """
    {"type":"object",
     "properties":{
       "primaryEntity":{"type":"string"},
       "relatedEntities":{"type":"array","items":{"type":"string"}},
       "mentions":{"type":"array","items":{"type":"object","properties":{"text":{"type":"string"},"entity":{"type":"string"}},"required":["text","entity"]}},
       "columns":{"type":"array","items":{"type":"string"}},
       "aggregate":{"type":"boolean"},
       "referencesPrevious":{"type":"boolean"},
       "referencesCurrentRecord":{"type":"boolean"}},
     "required":["primaryEntity","relatedEntities","mentions","columns","aggregate","referencesPrevious","referencesCurrentRecord"]}
    """;

    private const string SystemPrompt = """
    You analyze questions that business analysts ask about a Microsoft Dynamics 365 CRM. Reply with JSON only.
    Fields:
    - primaryEntity: logical name of the entity whose rows the user wants to see. It must be one of the listed entities. Use "" if unclear.
    - relatedEntities: logical names of other listed entities needed to filter or join (may be empty).
    - mentions: specific record names the user typed (companies, people, products). Each has the text exactly as typed and the logical name of the entity it belongs to. Do not include generic words.
      When the user asks to write or draft an email (or message) for a customer or contact, the email itself is not the data: primaryEntity is the account or contact the email is for (not an email entity) and that record's name belongs in mentions.
      Do NOT put filter values in mentions: cities, countries, dates, numbers, statuses and other attribute values are not records. For "accounts in Hamburg", "Hamburg" is a filter value, so mentions stays empty. A company whose own name contains a place (for example "Hamburg Logistik GmbH") is still a record and belongs in mentions.
      A name followed by a Turkish suffix after an apostrophe ("Örnekova'daki", "Örnekova'nın", "Örnekova'ya bağlı") that is followed by a record kind (contracts, contacts, opportunities, cases, orders, products...) names a RECORD (usually a company), not a place: "Örnekova'daki contractları getir" means the contracts of the company Örnekova, so mentions has Örnekova (entity account). Treat such a name as a place (a filter value, mentions empty) ONLY when the listed records are a kind that has an address and the question is about where they are, as in "Londra'daki firmalar".
    - columns: attribute names the user explicitly asked to see as columns (for example "show columns id, name", "sütun olarak name"). Attributes that are only used to filter, sort or limit the rows ("estimated close date in 2027", "price over 100", "top 3 by value") are NOT columns, even when the user names them. Empty array if the user did not say.
    - aggregate: true only if the user asks for a count, sum or average instead of rows, or asks for a chart, graph, distribution or breakdown (a chart shows counts or totals per category, never a list of records).
    The question may be in English or Turkish.
    """;

    private const string FollowUpRules = """

    The user may be continuing the previous question of this conversation (shown in the user message). Add "referencesPrevious" to the reply.
    - referencesPrevious is true ONLY when the new question clearly depends on the previous result: it points back at it ("bunlardan", "bu firmalar", "onların", "bu liste", "peki", "şimdi bu listeden", "these", "of those", "those companies") or it is a fragment that only makes sense as a refinement of it.
    - A question that names its own subject (a company, a record type) and stands on its own is NOT a follow-up, even if the topic is related. When unsure, use false.
    - If true: primaryEntity is the entity the user wants to see NOW; it may differ from the previous one ("their invoices" means invoice). Put the previous entity in relatedEntities when it is needed to reach the new one. In mentions, list only record names typed in the NEW question.
    - A word or fragment used to narrow the previous result ("only Line A", "the ones with Machine in the name", "those in London") is a filter value, not a record: do NOT put it in mentions.
    """;

    private const string CurrentRecordRules = """

    The user has a CRM record open in a form right now (shown in the user message). Add "referencesCurrentRecord" to the reply.
    - referencesCurrentRecord is true ONLY when the question clearly points at that open record: "bu müşteri", "bu müşterinin", "bu fırsat", "buradaki", "bu kayıt", "this account", "this customer", "this record" and similar. A question that names its own subject or asks about records in general ("all products", "Örnekova'nın contractları") is NOT about the open record. When unsure, use false.
    - If true: primaryEntity is the entity the user wants to see (for example "this customer's opportunities" means opportunity), and relatedEntities must include the open record's entity when it is needed to reach the answer. Do NOT put the open record in mentions.
    """;

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

    private readonly ILlmClient _llm;

    public LlmQuestionAnalyzer(ILlmClient llm)
    {
        _llm = llm;
    }

    /// <summary>
    /// Analyzes a question with the model, retrying once on invalid JSON.
    /// </summary>
    public Task<QuestionAnalysis> AnalyzeAsync(string question, IReadOnlyList<EntitySummary> entities, CancellationToken ct = default)
        => AnalyzeAsync(question, entities, new List<string>(), null, ct);

    /// <summary>
    /// Analyzes a question with the model, retrying once on invalid JSON.
    /// </summary>
    public Task<QuestionAnalysis> AnalyzeAsync(string question, IReadOnlyList<EntitySummary> entities, IReadOnlyList<string> knowledge, CancellationToken ct = default)
        => AnalyzeAsync(question, entities, knowledge, null, null, ct);

    /// <summary>
    /// Analyzes a question with the model, retrying once on invalid JSON.
    /// </summary>
    public Task<QuestionAnalysis> AnalyzeAsync(string question, IReadOnlyList<EntitySummary> entities, IReadOnlyList<string> knowledge, PreviousQuery? previous, CancellationToken ct = default)
        => AnalyzeAsync(question, entities, knowledge, previous, null, ct);

    /// <summary>
    /// Analyzes a question with the model, retrying once on invalid JSON.
    /// </summary>
    public Task<QuestionAnalysis> AnalyzeAsync(string question, IReadOnlyList<EntitySummary> entities, IReadOnlyList<string> knowledge, PreviousQuery? previous, ResolvedRecord? current, CancellationToken ct = default)
        => AnalyzeAsync(question, entities, knowledge, previous, current, Array.Empty<CrmNlQuery.Domain.Glossary.Entities.GlossaryHit>(), ct);

    /// <summary>
    /// Analyzes a question with the model, retrying once on invalid JSON.
    /// </summary>
    public async Task<QuestionAnalysis> AnalyzeAsync(string question, IReadOnlyList<EntitySummary> entities, IReadOnlyList<string> knowledge, PreviousQuery? previous, ResolvedRecord? current,
        IReadOnlyList<CrmNlQuery.Domain.Glossary.Entities.GlossaryHit> glossary, CancellationToken ct = default)
    {
        var lines = string.Join("\n", entities.Select(e => $"{e.LogicalName} | {e.Label}"));
        var sb = new StringBuilder();
        KnowledgePrompt.Append(sb, knowledge);
        GlossaryPrompt.AppendForAnalysis(sb, glossary);
        if (previous is not null)
        {
            sb.Append($"Previous question in this conversation (it showed {previous.Entity} records): \"{PromptText.Quote(previous.Question, 300)}\"\n\n");
        }
        if (current is not null)
        {
            sb.Append($"Open CRM record (the user is looking at this form): {current.Entity} \"{PromptText.Quote(current.Name, 120)}\"\n\n");
        }
        sb.Append($"Question: {question}");
        var user = sb.ToString();
        var system = SystemPrompt + "\n\nEntities (logical name | label):\n" + lines + "\n" + (previous is null ? "" : FollowUpRules) + (current is null ? "" : CurrentRecordRules);
        var schema = (previous is not null, current is not null) switch
        {
            (false, false) => Schema,
            (true, false) => SchemaWithPrevious,
            (false, true) => SchemaWithContext,
            _ => SchemaWithBoth,
        };
        var known = new HashSet<string>(entities.Select(e => e.LogicalName), StringComparer.OrdinalIgnoreCase);

        string? lastProblem = null;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var prompt = lastProblem is null ? user : user + $"\n\nYour previous reply was not usable ({lastProblem}). Reply again with valid JSON.";
            var raw = await _llm.CompleteJsonAsync(system, prompt, schema, ct);
            try
            {
                var dto = JsonSerializer.Deserialize<AnalysisDto>(raw, JsonOptions);
                if (dto is null)
                {
                    lastProblem = "empty reply";
                    continue;
                }
                return Map(dto, known, previous is not null, current is not null);
            }
            catch (JsonException ex)
            {
                lastProblem = ex.Message;
            }
        }
        throw new LlmOutputException("Question analysis failed: " + lastProblem);
    }

    private static QuestionAnalysis Map(AnalysisDto dto, HashSet<string> known, bool hasPrevious, bool hasCurrent)
    {
        var related = (dto.RelatedEntities ?? new List<string>())
            .Select(e => e.Trim().ToLowerInvariant()).Where(known.Contains).Distinct().ToList();
        var mentions = (dto.Mentions ?? new List<MentionDto>())
            .Where(m => !string.IsNullOrWhiteSpace(m.Text))
            .Select(m => new Mention(MentionText.StripTurkishSuffixes(m.Text!.Trim()), (m.Entity ?? "").Trim().ToLowerInvariant())).ToList();
        var columns = (dto.Columns ?? new List<string>()).Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()).ToList();
        return new QuestionAnalysis((dto.PrimaryEntity ?? "").Trim().ToLowerInvariant(), related, mentions, columns, dto.Aggregate, hasPrevious && dto.ReferencesPrevious, hasCurrent && dto.ReferencesCurrentRecord);
    }

    private sealed class AnalysisDto
    {
        public string? PrimaryEntity { get; set; }
        public List<string>? RelatedEntities { get; set; }
        public List<MentionDto>? Mentions { get; set; }
        public List<string>? Columns { get; set; }
        public bool Aggregate { get; set; }
        public bool ReferencesPrevious { get; set; }
        public bool ReferencesCurrentRecord { get; set; }
    }

    private sealed class MentionDto
    {
        public string? Text { get; set; }
        public string? Entity { get; set; }
    }
}
