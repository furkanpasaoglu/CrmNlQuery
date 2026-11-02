using System.Text.Json;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.Design.Models;
using CrmNlQuery.Domain.Metadata.Entities;

namespace CrmNlQuery.Application.Design.Services;

/// <summary>
/// Data model used by the customization design assistant for planner.
/// </summary>
public sealed class DesignPlanner
{
    private const int MaxJsonAttempts = 2;
    private const int MaxDesignRetries = 2;
    private const int MaxDetailLoads = 40;

    private const string ExtractionSchema = """
    {"type":"object","properties":{
      "language":{"type":"string"},
      "summary":{"type":"string"},
      "concepts":{"type":"array","items":{"type":"object","properties":{
        "term":{"type":"string"},
        "synonyms":{"type":"array","items":{"type":"string"}},
        "kind":{"type":"string","enum":["table","field","relationship"]}},
        "required":["term","synonyms","kind"]}},
      "standardTables":{"type":"array","items":{"type":"string"}}},
     "required":["language","summary","concepts","standardTables"]}
    """;

    private const string ReuseSchema = """
    {"type":"object","properties":{
      "decisions":{"type":"array","items":{"type":"object","properties":{
        "entity":{"type":"string"},
        "action":{"type":"string","enum":["extend","reuse","ignore"]},
        "reason":{"type":"string"}},
        "required":["entity","action","reason"]}}},
     "required":["decisions"]}
    """;

    private const string DesignSchema = """
    {"type":"object","properties":{
      "summary":{"type":"string"},
      "tables":{"type":"array","items":{"type":"object","properties":{
        "logicalName":{"type":"string"},
        "displayName":{"type":"string"},
        "pluralName":{"type":"string"},
        "ownership":{"type":"string","enum":["user","organization"]},
        "description":{"type":"string"},
        "extends":{"type":"string"},
        "primaryNameLogical":{"type":"string"},
        "primaryNameDisplay":{"type":"string"},
        "fields":{"type":"array","items":{"type":"object","properties":{
          "logicalName":{"type":"string"},
          "displayName":{"type":"string"},
          "type":{"type":"string","enum":["string","memo","integer","decimal","money","datetime","date","boolean","picklist","lookup"]},
          "required":{"type":"boolean"},
          "length":{"type":"integer"},
          "options":{"type":"array","items":{"type":"object","properties":{"value":{"type":"integer"},"label":{"type":"string"}},"required":["value","label"]}},
          "target":{"type":"string"},
          "description":{"type":"string"}},
          "required":["logicalName","displayName","type","required"]}}},
        "required":["logicalName","displayName","ownership","fields"]}},
      "relationships":{"type":"array","items":{"type":"object","properties":{
        "kind":{"type":"string","enum":["one-to-many","many-to-one","many-to-many"]},
        "from":{"type":"string"},
        "to":{"type":"string"},
        "lookupField":{"type":"string"},
        "deleteBehavior":{"type":"string","enum":["cascade","restrict","remove-link","none"]},
        "description":{"type":"string"}},
        "required":["kind","from","to"]}},
      "views":{"type":"array","items":{"type":"object","properties":{
        "table":{"type":"string"},"name":{"type":"string"},"columns":{"type":"array","items":{"type":"string"}}},
        "required":["table","name","columns"]}},
      "securityNotes":{"type":"array","items":{"type":"string"}},
      "openQuestions":{"type":"array","items":{"type":"string"}}},
     "required":["summary","tables","relationships","views","securityNotes","openQuestions"]}
    """;

    private const string ExtractionPrompt = """
    You help a CRM consultant analyse a new business requirement for Microsoft Dynamics 365 customization. Reply with JSON only.
    Read the requirement and return:
    - language: the language of the requirement as a short code ("tr", "en", ...).
    - summary: 2-4 sentences restating what the business wants, in the SAME language as the requirement. Do not add requirements the text does not contain.
    - concepts: the business things to look for in an existing CRM schema. Each has a term, up to 6 synonyms and a kind (table = a kind of record such as appointment or vehicle, field = a property such as status or date, relationship = a link between two kinds of record).
      Give BASE (singular, no suffix) forms and include English and local-language words, for example for "randevu": synonyms "appointment", "booking", "schedule".
    - standardTables: logical names of STANDARD Dynamics 365 tables that might already cover part of the requirement (for example incident for support cases and complaints, account, contact, opportunity, quote, salesorder, invoice, product, contract, lead, campaign, appointment, task, phonecall, email, annotation). Only tables you are fairly sure exist in standard Dynamics 365; at most 6.
    Only business concepts; no technical words.
    """;

    private const string ReusePrompt = """
    You decide whether an existing CRM table should be reused for a new requirement, instead of creating a new table. Reply with JSON only.
    You get the requirement and CANDIDATE tables found in the CRM schema by a search (with the fields that matched).
    For EVERY candidate return one decision:
    - extend: the candidate already holds the same kind of record, so add the missing fields to it instead of creating a new table.
    - reuse: the candidate is a related table that stays as it is; the new design links to it (for example a customer or a product table).
    - ignore: the match is accidental (similar word, different meaning).
    Give a short reason in the language of the requirement. Decide ONLY about the listed candidates; never invent tables.
    """;

    private const string DesignPrompt = """
    You are a senior Microsoft Dynamics 365 consultant. Design the customization for the requirement. Reply with JSON only.
    Rules:
    - Write summary, display names, descriptions, security notes and open questions in the language of the requirement. Logical names are ASCII, lower-case, with underscores.
    - Every NEW table and every NEW field has a logical name that starts with the publisher prefix given below (prefix + "_" + name), at most 50 characters. Never reuse an existing name.
    - A new table has ownership "user" for transactional records people work on, or "organization" for reference data. A new table needs primaryNameLogical (prefix_name style) and primaryNameDisplay; do NOT list the primary name again in fields.
    - Reuse decisions are given. For "extend": add a table entry whose logicalName AND extends are that existing table, listing ONLY the new fields it needs. For "reuse": do not recreate the table; point lookups and relationships to it. Create a new table only for a kind of record that does not exist yet.
    - Field types: string (set length 1-4000), memo, integer, decimal, money, datetime, date, boolean, picklist (give at least two options with values 1,2,3... and labels), lookup (set target to an existing or designed table's logical name).
    - Relationships: one-to-many (from = the "one" table, to = the "many" table) or many-to-one (from = the "many" table, to = the "one" table) need lookupField = the lookup field that lives on the MANY table; that table must be listed in tables (as a new table or as an extension) with that lookup field whose target is the "one" table. many-to-many has no lookupField. deleteBehavior: cascade when the child makes no sense without its parent (for example the lines of an order), restrict when the child must keep existing, remove-link or none otherwise.
    - views: at least one view per new table with 4-7 columns; use only logical names that exist on that table (the primary name, designed fields, or existing fields shown below).
    - securityNotes: mention fields that may hold personal or sensitive data and what to do about them. openQuestions: at least two real business questions the requirement leaves open (for example whether cancelled records are kept or deleted).
    - Keep the design as small as the requirement allows. No invented requirements.
    """;

    private readonly ILlmClient _llm;
    private readonly IMetadataSource _schema;
    private readonly Func<CancellationToken, Task<Func<string, bool>>>? _closedProvider;

    public DesignPlanner(ILlmClient llm, IMetadataSource schema, Func<CancellationToken, Task<Func<string, bool>>>? closedProvider = null)
    {
        _llm = llm;
        _schema = schema;
        _closedProvider = closedProvider;
    }

    /// <summary>
    /// Runs the full design pipeline (concepts, schema search, reuse decisions, design, validation with retries) and returns the result, reporting progress through the callback.
    /// </summary>
    public async Task<DesignResult> RunAsync(DesignRequest request, Action<string, int>? progress = null, CancellationToken ct = default)
    {
        void Report(string step, int percent) => progress?.Invoke(step, percent);

        Report("Understanding the request", 5);
        var extraction = await ExtractAsync(request.Text, ct);

        Report("Searching the CRM schema", 25);
        var entities = await _schema.ListEntitiesAsync(ct);
        var prefix = string.IsNullOrWhiteSpace(request.Prefix) ? SchemaSearch.DetectPrefix(entities.Select(e => e.LogicalName)) : request.Prefix.Trim().ToLowerInvariant();
        var hits = SchemaSearch.ScoreEntities(entities, extraction.Concepts, extraction.StandardTables);
        var toLoad = hits.Take(8).Select(h => h.Entity)
            .Concat(entities.Where(e => SchemaSearch.IsCustom(e.LogicalName)).Select(e => e.LogicalName))
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxDetailLoads).ToList();
        var detailed = new List<EntityInfo>();
        foreach (var name in toLoad)
        {
            var info = await _schema.GetEntityAsync(name, ct);
            if (info is not null)
            {
                detailed.Add(info);
            }
        }
        var matches = SchemaSearch.Match(detailed, hits, extraction.Concepts);
        var context = new DesignContext
        {
            Prefix = prefix,
            ExistingEntities = new HashSet<string>(entities.Select(e => e.LogicalName), StringComparer.Ordinal),
            Detailed = detailed.ToDictionary(d => d.LogicalName, d => d, StringComparer.Ordinal),
            CandidateNames = new HashSet<string>(matches.Select(m => m.Entity), StringComparer.Ordinal),
        };

        Report("Deciding what to reuse", 45);
        var decisions = matches.Count == 0 ? new List<ReuseDecision>() : await DecideAsync(request.Text, extraction, matches, context, ct);

        Report("Designing", 60);
        var (document, problems) = await DesignAsync(request.Text, extraction, prefix, matches, decisions, context, Report, ct);

        Report("Finishing", 95);
        var isClosed = _closedProvider is null ? null : await _closedProvider(ct);
        var closed = isClosed is null ? new List<string>() : matches.Where(m => isClosed(m.Entity)).Select(m => m.Entity).ToList();
        return new DesignResult
        {
            Language = extraction.Language,
            Prefix = prefix,
            Request = request.Text,
            Document = document,
            Matches = matches,
            Decisions = decisions,
            Problems = problems,
            ClosedTables = closed,
        };
    }

    private async Task<DesignExtraction> ExtractAsync(string text, CancellationToken ct)
    {
        string? problem = null;
        for (var attempt = 1; attempt <= MaxJsonAttempts; attempt++)
        {
            var user = "Requirement:\n" + text + (problem is null ? "" : $"\n\nYour previous reply was not usable ({problem}). Reply again with valid JSON.");
            var raw = await _llm.CompleteJsonAsync(ExtractionPrompt, user, ExtractionSchema, ct);
            try
            {
                var extraction = JsonSerializer.Deserialize<DesignExtraction>(raw, DesignJson.Read);
                if (extraction is not null && extraction.Concepts.Count > 0)
                {
                    extraction.Concepts = extraction.Concepts.Where(c => !string.IsNullOrWhiteSpace(c.Term)).ToList();
                    if (extraction.Concepts.Count > 0)
                    {
                        return extraction;
                    }
                }
                problem = "no concepts";
            }
            catch (JsonException ex)
            {
                problem = ex.Message;
            }
        }
        var words = text.Split(new[] { ' ', ',', '.', ';', ':', '\n', '\r', '(', ')' }, StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length >= 5).Distinct().Take(8);
        return new DesignExtraction { Language = "en", Summary = text, Concepts = words.Select(w => new DesignConcept { Term = w, Kind = "table" }).ToList() };
    }

    private async Task<List<ReuseDecision>> DecideAsync(string text, DesignExtraction extraction, IReadOnlyList<SchemaMatch> matches, DesignContext context, CancellationToken ct)
    {
        var candidates = JsonSerializer.Serialize(matches.Select(m => new
        {
            entity = m.Entity, label = m.Label, custom = m.IsCustom, fields = m.AttributeCount,
            matchedFields = m.Fields.Select(f => $"{f.Logical} ({f.Label}, {f.Type})"),
            customFields = m.CustomFields,
        }), DesignJson.Write);
        string? problems = null;
        List<ReuseDecision> last = new List<ReuseDecision>();
        for (var attempt = 1; attempt <= MaxJsonAttempts; attempt++)
        {
            var user = $"Requirement:\n{text}\n\nSummary: {extraction.Summary}\n\nCandidate tables:\n{candidates}" + (problems is null ? "" : $"\n\nYour previous reply had problems:\n{problems}\nReply again.");
            var raw = await _llm.CompleteJsonAsync(ReusePrompt, user, ReuseSchema, ct);
            try
            {
                var response = JsonSerializer.Deserialize<ReuseResponse>(raw, DesignJson.Read) ?? new ReuseResponse();
                var errors = DesignValidator.ValidateDecisions(response.Decisions, context);
                if (errors.Count == 0)
                {
                    return response.Decisions;
                }
                last = response.Decisions.Where(d => context.CandidateNames.Contains(d.Entity) && new[] { "extend", "reuse", "ignore" }.Contains(d.Action)).ToList();
                problems = string.Join("\n", errors.Select(e => "- " + e));
            }
            catch (JsonException ex)
            {
                problems = "- " + ex.Message;
            }
        }
        return last;
    }

    /// <summary>
    /// Repairs a common local-model inconsistency: when a candidate table has an extend decision but empty extends, sets extends to its logical name; ignore decisions are untouched.
    /// </summary>
    public static void NormalizeExtensions(DesignDocument document, IReadOnlyList<ReuseDecision> decisions, DesignContext context)
    {
        foreach (var table in document.Tables)
        {
            var name = table.LogicalName ?? "";
            if (string.IsNullOrWhiteSpace(table.Extends) && context.CandidateNames.Contains(name) && context.ExistingEntities.Contains(name)
                && decisions.Any(d => d.Action == "extend" && string.Equals(d.Entity, name, StringComparison.Ordinal)))
            {
                table.Extends = name;
                table.PrimaryNameLogical = "";
                table.PrimaryNameDisplay = "";
            }
        }
    }

    private async Task<(DesignDocument Document, IReadOnlyList<string> Problems)> DesignAsync(string text, DesignExtraction extraction, string prefix,
        IReadOnlyList<SchemaMatch> matches, IReadOnlyList<ReuseDecision> decisions, DesignContext context,
        Action<string, int> report, CancellationToken ct)
    {
        var used = decisions.Where(d => d.Action != "ignore").Select(d => d.Entity).ToHashSet(StringComparer.Ordinal);
        var existing = JsonSerializer.Serialize(matches.Where(m => used.Contains(m.Entity)).Select(m => new
        {
            entity = m.Entity, label = m.Label, fieldsAlreadyThere = m.CustomFields, matchedFields = m.Fields.Select(f => f.Logical),
        }), DesignJson.Write);
        var common = new[] { "account", "contact", "systemuser", "opportunity", "incident", "product", "team" }.Where(n => context.ExistingEntities.Contains(n));
        var baseUser = $"Requirement:\n{text}\n\nLanguage: {extraction.Language}\nPublisher prefix: {prefix}\nSummary: {extraction.Summary}\n\n" +
                       $"Reuse decisions:\n{JsonSerializer.Serialize(decisions.Select(d => new { entity = d.Entity, action = d.Action, reason = d.Reason }), DesignJson.Write)}\n\n" +
                       $"Existing tables you may extend or link to (with the fields they already have):\n{existing}\n\nStandard tables you may point lookups at: {string.Join(", ", common)}";

        string? feedback = null;
        DesignDocument best = new DesignDocument();
        IReadOnlyList<string> bestErrors = new List<string> { "The model did not return a usable design." };
        for (var attempt = 0; attempt <= MaxDesignRetries; attempt++)
        {
            report(attempt == 0 ? "Designing" : "Fixing the design", 60 + attempt * 10);
            var user = feedback is null ? baseUser : baseUser + "\n\nThe previous design had these problems:\n" + feedback + "\nReturn the COMPLETE corrected design JSON.";
            var raw = await _llm.CompleteJsonAsync(DesignPrompt, user, DesignSchema, ct);
            try
            {
                var document = JsonSerializer.Deserialize<DesignDocument>(raw, DesignJson.Read);
                if (document is null)
                {
                    feedback = "- The reply was empty.";
                    continue;
                }
                NormalizeExtensions(document, decisions, context);
                var errors = DesignValidator.Validate(document, context);
                if (errors.Count == 0)
                {
                    return (document, new List<string>());
                }
                if (bestErrors.Count > 0 && (best.Tables.Count == 0 || errors.Count <= bestErrors.Count))
                {
                    best = document;
                    bestErrors = errors;
                }
                feedback = string.Join("\n", errors.Take(25).Select(e => "- " + e));
            }
            catch (JsonException ex)
            {
                feedback = "- The reply was not valid JSON: " + ex.Message;
            }
        }
        return (best, bestErrors);
    }
}
