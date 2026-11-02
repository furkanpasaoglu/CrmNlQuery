using CrmNlQuery.Application.Observability.Calls;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.Ask.Services;
using CrmNlQuery.Application.Knowledge.Models;
using CrmNlQuery.Application.Knowledge.Services;
using CrmNlQuery.Application.Operations.Models;
using CrmNlQuery.Domain.Access.Constants;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Infrastructure.External.Crm;
using CrmNlQuery.Infrastructure.External.Crm.Metadata;
using CrmNlQuery.Infrastructure.External.Crm.Records;
using CrmNlQuery.Infrastructure.External.Llm;
using CrmNlQuery.Infrastructure.Persistence.Memory.Knowledge;

namespace CrmNlQuery.Composition;

/// <summary>
/// Composes the eval command-line pipeline with the chosen model provider and the plan cache turned off so repeated measurements really call the model.
/// </summary>
public static class EvalComposition
{
    /// <summary>
    /// Holds the document ranks of one retrieval trace for the vector, text and fused search methods.
    /// </summary>
    public sealed record RetrievalTrace(IReadOnlyList<string> Vector, IReadOnlyList<string> Text, IReadOnlyList<string> Fused,
        IReadOnlyList<string> FusedChunks, IReadOnlyList<string> VectorChunks, IReadOnlyList<string> TextChunks);

    /// <summary>
    /// Eval knowledge base that indexes documents in memory with the real local embedding model and leaves nothing behind.
    /// </summary>
    public sealed class EvalKnowledge
    {
        private readonly RecordingSource _source;
        private readonly KnowledgeService _service;

        internal EvalKnowledge(RecordingSource source, KnowledgeService service, int documents)
        {
            _source = source;
            _service = service;
            Documents = documents;
        }

        /// <summary>
        /// Returns the chunk texts that would really be given to the model, using the same path as the production knowledge retriever.
        /// </summary>
        public Task<IReadOnlyList<string>> NotesAsync(string question, CancellationToken ct = default) => _source.FindAsync(question, ct);

        /// <summary>
        /// Returns, for a question, the document title rankings of the vector, text and fused methods, counting each document once.
        /// </summary>
        public async Task<RetrievalTrace> TraceAsync(string question, int candidates, CancellationToken ct = default)
        {
            var trace = await _service.TraceAsync(question, candidates, ct);
            static IReadOnlyList<string> Titles(IEnumerable<CrmNlQuery.Domain.Knowledge.Entities.KnowledgeSnippet> snippets) => snippets.Select(s => s.Title).Distinct().ToList();
            return new RetrievalTrace(Titles(trace.Vector), Titles(trace.Text), Titles(trace.Fused),
                trace.Fused.Select(x => x.Text).ToList(), trace.Vector.Select(x => x.Text).ToList(), trace.Text.Select(x => x.Text).ToList());
        }

        public IKnowledgeSource Source => _source;
        public int Documents { get; }

        /// <summary>
        /// Returns and clears the titles of the chunks most recently given to the model, for debugging.
        /// </summary>
        public IReadOnlyList<string> TakeFoundTitles() => _source.Take();
    }

    /// <summary>
    /// Knowledge source wrapper that records which chunks were returned for each search.
    /// </summary>
    internal sealed class RecordingSource : IKnowledgeSource
    {
        private readonly IKnowledgeSource _inner;
        private readonly List<string> _titles = new List<string>();

        public RecordingSource(IKnowledgeSource inner)
        {
            _inner = inner;
        }

        /// <summary>
        /// Finds knowledge chunks for the question and records their titles.
        /// </summary>
        public async Task<IReadOnlyList<string>> FindAsync(string question, CancellationToken ct = default)
        {
            var notes = await _inner.FindAsync(question, ct);
            lock (_titles)
            {
                _titles.AddRange(notes.Select(n => n.Split('\n')[0]));
            }
            return notes;
        }

        /// <summary>
        /// Returns and clears the recorded chunk titles.
        /// </summary>
        public IReadOnlyList<string> Take()
        {
            lock (_titles)
            {
                var copy = _titles.Distinct().ToList();
                _titles.Clear();
                return copy;
            }
        }
    }

    /// <summary>
    /// Indexes the given documents with the local embedding model using the Knowledge settings, and returns the eval knowledge base.
    /// </summary>
    public static async Task<EvalKnowledge> CreateKnowledgeAsync(IConfiguration configuration, IReadOnlyList<(string Title, string Body)> documents, CancellationToken ct = default)
    {
        var options = configuration.GetSection("Knowledge").Get<KnowledgeOptions>() ?? new KnowledgeOptions();
        var service = new KnowledgeService(new MemoryKnowledgeStore(), OllamaEmbeddingClient.Create(OllamaSettings(configuration), options.EmbeddingModel),
            options, (entry, token) => Task.CompletedTask);
        var actor = new AccessPrincipal("eval", "eval", new[] { AccessRoles.AdministratorName }, true, new string[0]);
        foreach (var (title, body) in documents)
        {
            var result = await service.CreateAsync(actor, title, body, ct);
            if (!result.Ok)
            {
                throw new InvalidOperationException($"Could not index the knowledge document '{title}': {result.Message}");
            }
        }
        return new EvalKnowledge(new RecordingSource(new KnowledgeRetriever(service, true, NullLogger.Instance)), service, documents.Count);
    }

    /// <summary>
    /// Returns the default model for a provider, from the configured Ollama model or the Claude default.
    /// </summary>
    public static string DefaultModel(IConfiguration configuration, string provider)
    {
        return provider switch
        {
            "anthropic" => new AnthropicOptions().Model,
            "vllm" => VllmSettings(configuration).Model,
            _ => OllamaSettings(configuration).Model,
        };
    }

    /// <summary>
    /// Checks whether a Claude API key is available from the environment, since cloud use is eval-only and lab-data-only.
    /// </summary>
    public static bool HasAnthropicKey(IConfiguration configuration)
    {
        return !string.IsNullOrWhiteSpace(configuration["Anthropic:ApiKey"]);
    }

    /// <summary>
    /// Builds the question pipeline for eval with the given provider and model, using company knowledge only when a knowledge source is supplied.
    /// </summary>
    public static AskPipeline CreatePipeline(IConfiguration configuration, string provider, string model, bool? think, IKnowledgeSource? knowledge = null,
        Microsoft.Extensions.Logging.ILogger<AskPipeline>? logger = null, LlmCallLog? calls = null, bool analysisCache = false, bool pruneContext = true, CrmNlQuery.Application.DataPolicy.Abstractions.IDataPolicy? dataPolicy = null, bool summary = false, bool draft = false, string? glossaryFile = null)
    {
        var crm = LabCrmOptions(configuration);

        ILlmClient anthropic = provider == "anthropic" ? AnthropicClient.Create(configuration["Anthropic:ApiKey"] ?? "", model) : null!;
        Func<string, ILlmClient> stepClient = provider == "anthropic" ? (_ => anthropic) : (purpose => LocalClient(configuration, provider, model, think, purpose));
        var llm = stepClient("plan");
        Func<string, ILlmClient>? forPurpose = calls is null ? (provider == "anthropic" ? null : stepClient) : purpose => new RecordingLlmClient(stepClient(purpose), calls, purpose);
        return PipelineFactory.Create(crm, llm, out _, forPurpose, logger, knowledge: knowledge, limits: Limits(configuration), evaluation: true,
            analysisCacheTtl: analysisCache ? TimeSpan.FromMinutes(10) : null, pruneContext: pruneContext, dataPolicy: dataPolicy,
            summaryFactory: summary ? (schema => PipelineFactory.CreateSummaryBuilder(crm, schema, null, () => TimeSpan.FromSeconds(15))) : null,
            summaryEnabled: summary ? (() => true) : (() => false),
            draftFactory: draft ? ((schema, executor, draftClient) => PipelineFactory.CreateDraftService(schema, executor, draftClient)) : null,
            draftEnabled: draft ? (() => true) : (() => false),
            glossaryFactory: glossaryFile is null ? null : (schema => LoadGlossary(glossaryFile, schema)));
    }

    private static CrmNlQuery.Application.Glossary.Services.GlossaryService LoadGlossary(string path, IMetadataSource schema)
    {
        var store = new MemoryGlossaryStore();
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
        var now = DateTimeOffset.UtcNow;
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var terms = item.GetProperty("terms").EnumerateArray().Select(t => new CrmNlQuery.Domain.Glossary.Entities.GlossaryTerm(t.GetString() ?? "")).ToList();
            var conditions = new List<CrmNlQuery.Domain.Glossary.Entities.GlossaryCondition>();
            if (item.TryGetProperty("conditions", out var cs))
            {
                foreach (var c in cs.EnumerateArray())
                {
                    var values = c.TryGetProperty("values", out var vs) ? vs.EnumerateArray().Select(x => x.GetString() ?? "").ToList() : new List<string>();
                    conditions.Add(new CrmNlQuery.Domain.Glossary.Entities.GlossaryCondition(c.GetProperty("attribute").GetString() ?? "", c.GetProperty("operator").GetString() ?? "", values));
                }
            }
            var entry = new CrmNlQuery.Domain.Glossary.Entities.GlossaryEntry
            {
                Kind = item.GetProperty("kind").GetString() ?? "",
                Terms = terms,
                Entity = item.GetProperty("entity").GetString() ?? "",
                Attribute = item.TryGetProperty("attribute", out var at) ? at.GetString() : null,
                Conditions = conditions,
                Description = item.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "",
                CreatedAt = now, ModifiedAt = now, CreatedBy = "eval", ModifiedBy = "eval",
            };
            store.AddAsync(entry).GetAwaiter().GetResult();
        }
        return new CrmNlQuery.Application.Glossary.Services.GlossaryService(store, schema, () => true);
    }

    /// <summary>
    /// Builds the eval Ollama options from configuration plus the chosen model and thinking setting, keeping every field including the context window.
    /// </summary>
    public static OllamaOptions OllamaOptionsFor(IConfiguration configuration, string model, bool? think)
    {
        var ollamaBase = OllamaSettings(configuration);
        return new OllamaOptions
        {
            BaseUrl = ollamaBase.BaseUrl,
            Model = model,
            Think = think ?? ollamaBase.Think,
            TimeoutMinutes = ollamaBase.TimeoutMinutes,
            AllowRemoteHost = ollamaBase.AllowRemoteHost,
            TrustedHosts = ollamaBase.TrustedHosts,
            NumCtx = ollamaBase.NumCtx,
            KeepAlive = ollamaBase.KeepAlive,
            ThinkAnalyze = think is null ? ollamaBase.ThinkAnalyze : null,
            ThinkDecompose = think is null ? ollamaBase.ThinkDecompose : null,
            ThinkPlan = think is null ? ollamaBase.ThinkPlan : null,
        };
    }

    /// <summary>
    /// Returns the local model context window in tokens, where 0 means none is sent, used to decide whether a prompt would overflow.
    /// </summary>
    public static int ContextWindow(IConfiguration configuration) => OllamaSettings(configuration).NumCtx;

    /// <summary>
    /// Builds only the question decomposer for measuring the trigger and split decision without calling the CRM.
    /// </summary>
    public static CrmNlQuery.Application.Ask.Abstractions.IQuestionDecomposer CreateDecomposer(IConfiguration configuration, string provider, string model, bool? think)
    {
        if (provider == "anthropic")
        {
            return new CrmNlQuery.Application.Ask.Planning.LlmQuestionDecomposer(AnthropicClient.Create(configuration["Anthropic:ApiKey"] ?? "", model));
        }
        return new CrmNlQuery.Application.Ask.Planning.LlmQuestionDecomposer(LocalClient(configuration, provider, model, think));
    }

    /// <summary>
    /// Builds the extractor and schema source for design and record-creation measurements using the unfiltered lab metadata; nothing is written to the CRM.
    /// </summary>
    public static (CrmNlQuery.Application.Create.Services.CreateExtractor Extractor, CrmNlQuery.Application.Create.Abstractions.ICreateSchemaSource Schemas) CreateRecordCreationParts(IConfiguration configuration, string provider, string model, bool? think)
    {
        var crm = LabCrmOptions(configuration);
        CrmNlQuery.Application.Ask.Abstractions.ILlmClient client = provider == "anthropic"
            ? AnthropicClient.Create(configuration["Anthropic:ApiKey"] ?? "", model)
            : LocalClient(configuration, provider, model, think, "create");
        PipelineFactory.Create(crm, client, out var metadata);
        return (new CrmNlQuery.Application.Create.Services.CreateExtractor(client), new CrmCreateSchemaProvider(CrmClient.Create(crm), metadata));
    }

    /// <summary>
    /// Builds the customization design assistant for eval with the chosen model.
    /// </summary>
    public static CrmNlQuery.Application.Design.Services.DesignPlanner CreateDesigner(IConfiguration configuration, string provider, string model, bool? think)
    {
        var crm = LabCrmOptions(configuration);

        CrmNlQuery.Application.Ask.Abstractions.ILlmClient client = provider == "anthropic"
            ? AnthropicClient.Create(configuration["Anthropic:ApiKey"] ?? "", model)
            : LocalClient(configuration, provider, model, think, "design");
        PipelineFactory.Create(crm, client, out var metadata);
        return new CrmNlQuery.Application.Design.Services.DesignPlanner(client, metadata);
    }

    /// <summary>
    /// Builds a function that analyzes a question with the real analyzer and CRM metadata, without fetching row data.
    /// </summary>
    public static Func<string, Task<CrmNlQuery.Domain.Ask.Entities.QuestionAnalysis>> CreateAnalyzer(IConfiguration configuration, string provider, string model, bool? think)
    {
        var crm = LabCrmOptions(configuration);

        CrmNlQuery.Application.Ask.Abstractions.ILlmClient client;
        if (provider == "anthropic")
        {
            client = AnthropicClient.Create(configuration["Anthropic:ApiKey"] ?? "", model);
        }
        else
        {
            client = LocalClient(configuration, provider, model, think);
        }
        PipelineFactory.Create(crm, client, out var metadata);
        var analyzer = new CrmNlQuery.Application.Ask.Planning.LlmQuestionAnalyzer(client);
        return async question => await analyzer.AnalyzeAsync(question, await metadata.ListEntitiesAsync());
    }

    /// <summary>
    /// Loads an eval data policy allowlist from a JSON file of default tables and table or field rules.
    /// </summary>
    public static CrmNlQuery.Application.DataPolicy.Abstractions.IDataPolicy LoadPolicy(string path)
    {
        var file = System.Text.Json.JsonSerializer.Deserialize<PolicyFile>(File.ReadAllText(path), new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var rules = (file.Rules ?? new List<PolicyFileRule>())
            .Select(r => CrmNlQuery.Application.DataPolicy.Services.DataPolicyRules.Normalize(new DataPolicyRule(r.Kind ?? "", r.Entity ?? "", r.Attribute, r.Allowed, "eval", DateTimeOffset.UtcNow)))
            .ToList();
        var defaults = (file.DefaultTables is { Count: > 0 } ? file.DefaultTables : CrmNlQuery.Application.DataPolicy.Services.DataPolicyDefaults.Tables.ToList()).Select(x => x.ToLowerInvariant()).ToList();
        return new FixedPolicy(new CrmNlQuery.Application.DataPolicy.Services.DataPolicySnapshot(rules, defaults));
    }

    /// <summary>
    /// Builds a model-free checker that tells whether a stored FetchXML is still allowed by the data policy against the real lab schema.
    /// </summary>
    public static Func<string, CancellationToken, Task<bool>> CreateStoredQueryChecker(IConfiguration configuration, CrmNlQuery.Application.DataPolicy.Abstractions.IDataPolicy policy)
    {
        var crm = LabCrmOptions(configuration);
        var client = CrmClient.Create(crm, allowEvaluationCaller: true);
        var source = new CrmNlQuery.Application.DataPolicy.Services.PolicyMetadataSource(new MetadataProvider(client, crm.MetadataCacheDir, () => DateTimeOffset.UtcNow), policy);
        return (fetchXml, ct) => CrmNlQuery.Application.DataPolicy.Services.StoredQueryCheck.IsUsableAsync(fetchXml, source, ct);
    }

    private sealed class FixedPolicy : CrmNlQuery.Application.DataPolicy.Abstractions.IDataPolicy
    {
        private readonly CrmNlQuery.Application.DataPolicy.Services.DataPolicySnapshot _snapshot;
        public FixedPolicy(CrmNlQuery.Application.DataPolicy.Services.DataPolicySnapshot snapshot) { _snapshot = snapshot; }
        /// <summary>
        /// Returns the data policy snapshot used by the eval checker.
        /// </summary>
        public Task<CrmNlQuery.Application.DataPolicy.Services.DataPolicySnapshot> GetAsync(CancellationToken ct = default) => Task.FromResult(_snapshot);
    }

    private sealed class PolicyFile
    {
        public List<string>? DefaultTables { get; set; }
        public List<PolicyFileRule>? Rules { get; set; }
    }

    private sealed class PolicyFileRule
    {
        public string? Kind { get; set; }
        public string? Entity { get; set; }
        public string? Attribute { get; set; }
        public bool Allowed { get; set; }
    }

    /// <summary>
    /// Builds a finder that resolves an entity and record name into the first matching record as an open-form context record.
    /// </summary>
    public static Func<string, string, CancellationToken, Task<ResolvedRecord?>> CreateContextFinder(IConfiguration configuration)
    {
        var crm = LabCrmOptions(configuration);
        var client = CrmClient.Create(crm, allowEvaluationCaller: true);
        var metadata = new MetadataProvider(client, crm.MetadataCacheDir, () => DateTimeOffset.UtcNow);
        var searcher = new CrmRecordSearcher(new FetchQueryExecutor(client));
        return async (entity, name, ct) =>
        {
            var info = await metadata.GetEntityAsync(entity, ct);
            if (info is null) { return null; }
            var matches = await searcher.SearchByNameAsync(info, name, CrmNlQuery.Domain.Access.Entities.Caller.ForEvaluation(), ct);
            var exact = matches.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)) ?? matches.FirstOrDefault();
            return exact is null ? null : new ResolvedRecord("this record", info.LogicalName, exact.Id, exact.Name);
        };
    }

    /// <summary>
    /// Builds a runner that executes a reference FetchXML on current data with the same identity, executor and table builder as the candidate.
    /// </summary>
    public static Func<string, CancellationToken, Task<ResultTable>> CreateReferenceRunner(IConfiguration configuration)
    {
        var crm = LabCrmOptions(configuration);
        var client = CrmClient.Create(crm, allowEvaluationCaller: true);
        var metadata = new MetadataProvider(client, crm.MetadataCacheDir, () => DateTimeOffset.UtcNow);
        var executor = new FetchQueryExecutor(client);
        return async (fetchXml, ct) =>
        {
            var entityName = (string?)System.Xml.Linq.XDocument.Parse(fetchXml).Root!.Element("entity")?.Attribute("name")
                ?? throw new InvalidOperationException("The reference fetchXml has no <entity>.");
            var info = await metadata.GetEntityAsync(entityName, ct)
                ?? throw new InvalidOperationException($"Unknown entity '{entityName}' in the reference.");
            return await executor.ExecuteAsync(info.EntitySetName, fetchXml, CrmNlQuery.Domain.Access.Entities.Caller.ForEvaluation(), ct);
        };
    }

    /// <summary>
    /// Builds the metadata source and read-only aggregate runner used to gather facts for starter acceptance-set generation.
    /// </summary>
    public static (CrmNlQuery.Application.Ask.Abstractions.IMetadataSource Metadata, Func<string, CancellationToken, Task<ResultTable>> Run) CreateSchemaTools(IConfiguration configuration, CrmNlQuery.Application.DataPolicy.Abstractions.IDataPolicy? policy)
    {
        var crm = LabCrmOptions(configuration);
        var client = CrmClient.Create(crm, allowEvaluationCaller: true);
        var raw = new MetadataProvider(client, crm.MetadataCacheDir, () => DateTimeOffset.UtcNow);
        CrmNlQuery.Application.Ask.Abstractions.IMetadataSource metadata = policy is null ? raw : new CrmNlQuery.Application.DataPolicy.Services.PolicyMetadataSource(raw, policy);
        var executor = new FetchQueryExecutor(client);
        Func<string, CancellationToken, Task<ResultTable>> run = async (fetchXml, ct) =>
        {
            var entityName = (string?)System.Xml.Linq.XDocument.Parse(fetchXml).Root!.Element("entity")?.Attribute("name")
                ?? throw new InvalidOperationException("The query has no <entity>.");
            var info = await raw.GetEntityAsync(entityName, ct) ?? throw new InvalidOperationException($"Unknown entity '{entityName}'.");
            return await executor.ExecuteAsync(info.EntitySetName, fetchXml, CrmNlQuery.Domain.Access.Entities.Caller.ForEvaluation(), ct);
        };
        return (metadata, run);
    }

    private static FixedLimits Limits(IConfiguration configuration)
    {
        return new FixedLimits(configuration.GetSection("Limits").Get<GuardrailLimits>() ?? new GuardrailLimits());
    }

    private static OllamaOptions OllamaSettings(IConfiguration configuration)
    {
        return configuration.GetSection("Ollama").Get<OllamaOptions>() ?? new OllamaOptions();
    }

    private static VllmOptions VllmSettings(IConfiguration configuration)
    {
        return configuration.GetSection("Vllm").Get<VllmOptions>() ?? new VllmOptions();
    }

    /// <summary>
    /// Builds the eval vLLM options from configuration plus the chosen model and thinking setting, following the same rule as Ollama.
    /// </summary>
    public static VllmOptions VllmOptionsFor(IConfiguration configuration, string model, bool? think)
    {
        var baseOptions = VllmSettings(configuration);
        return new VllmOptions
        {
            BaseUrl = baseOptions.BaseUrl,
            Model = model,
            ApiKey = baseOptions.ApiKey,
            Think = think ?? baseOptions.Think,
            MaxModelLen = baseOptions.MaxModelLen,
            TimeoutMinutes = baseOptions.TimeoutMinutes,
            AllowRemoteHost = baseOptions.AllowRemoteHost,
            TrustedHosts = baseOptions.TrustedHosts,
            ThinkAnalyze = think is null ? baseOptions.ThinkAnalyze : null,
            ThinkDecompose = think is null ? baseOptions.ThinkDecompose : null,
            ThinkPlan = think is null ? baseOptions.ThinkPlan : null,
            ThinkCreate = think is null ? baseOptions.ThinkCreate : null,
        };
    }

    private static ILlmClient LocalClient(IConfiguration configuration, string provider, string model, bool? think, string? purpose = null)
    {
        if (provider == "vllm")
        {
            var vllm = VllmOptionsFor(configuration, model, think);
            return VllmClient.Create(purpose is null ? vllm : vllm.ForStep(purpose));
        }
        var ollama = OllamaOptionsFor(configuration, model, think);
        return OllamaClient.Create(purpose is null ? ollama : ollama.ForStep(purpose));
    }

    private static CrmOptions LabCrmOptions(IConfiguration configuration)
    {
        var crm = configuration.GetSection("Crm").Get<CrmOptions>() ?? new CrmOptions();
        if (string.IsNullOrEmpty(crm.BaseUrl)) { crm.BaseUrl = "http://crm.example.com/contoso"; }
        if (string.IsNullOrEmpty(crm.Domain)) { crm.Domain = "CONTOSO"; }
        if (string.IsNullOrEmpty(crm.Username)) { crm.Username = "Administrator"; }
        return crm;
    }
}
