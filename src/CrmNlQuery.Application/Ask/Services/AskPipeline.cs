using System.Xml.Linq;
using CrmNlQuery.Application.Ask.Planning;
using CrmNlQuery.Application.Ask.Planning.Validation;
using CrmNlQuery.Domain.Ask.Steps;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using CrmNlQuery.Application.Ask.Steps;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.Ask.Models;
using CrmNlQuery.Application.Counting.Services;
using CrmNlQuery.Application.DataPolicy.Services;
using CrmNlQuery.Application.Localization.Models;
using CrmNlQuery.Application.Localization.Services;
using CrmNlQuery.Application.Observability.Services;
using CrmNlQuery.Application.Operations.Abstractions;
using CrmNlQuery.Application.Operations.Models;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Domain.Common.Services;
using CrmNlQuery.Domain.Metadata.Entities;
using CrmNlQuery.Application.Common.Exceptions;

namespace CrmNlQuery.Application.Ask.Services;

/// <summary>
/// Runs the question pipeline: analyzes, resolves records, plans, validates and runs queries with the caller's permissions, returning an answer outcome.
/// </summary>
public sealed class AskPipeline
{
    public const string NeedsStepsKind = "needs_steps";
    public const string CannotAnswerKind = "no_matching_field";

    private readonly IMetadataSource _metadata;
    private readonly IQuestionAnalyzer _analyzer;
    private readonly IRecordSearcher _searcher;
    private readonly ColumnPolicy _columns;
    private readonly IPlanGenerator _planner;
    private readonly IQueryExecutor _executor;
    private readonly IQuestionDecomposer? _decomposer;
    private readonly IKnowledgeSource? _knowledge;
    private readonly ILimitsSource _limits;
    private readonly ILogger<AskPipeline> _logger;
    private readonly StepInputFiller? _stepFiller;
    private readonly StepIdColumn? _stepIdColumn;
    private readonly AnalysisCache? _analysisCache;
    private readonly CrmNlQuery.Application.Summary.Services.SummaryBuilder? _summary;
    private readonly Func<bool> _summaryEnabled;
    private readonly CrmNlQuery.Application.Drafts.Services.DraftService? _draft;
    private readonly Func<bool> _draftEnabled;
    private readonly CrmNlQuery.Application.Glossary.Services.GlossaryService? _glossary;
    private static readonly TimeSpan SchemaRefreshGap = TimeSpan.FromMinutes(5);
    private readonly bool _refreshSchemaWhenUnknown;
    private readonly IReadOnlyList<string> _exampleQuestionsGeneral;
    private readonly IReadOnlyList<string> _exampleQuestionsTurkish;
    private readonly Func<DateTimeOffset> _clock;
    private readonly object _schemaGate = new object();
    private DateTimeOffset _lastSchemaRefresh = DateTimeOffset.MinValue;

    public AskPipeline(IMetadataSource metadata, IQuestionAnalyzer analyzer, IRecordSearcher searcher,
        ColumnPolicy columns, IPlanGenerator planner, IQueryExecutor executor, IQuestionDecomposer? decomposer = null,
        ILogger<AskPipeline>? logger = null, IKnowledgeSource? knowledge = null, ILimitsSource? limits = null,
        StepInputFiller? stepFiller = null, StepIdColumn? stepIdColumn = null, AnalysisCache? analysisCache = null,
        CrmNlQuery.Application.Summary.Services.SummaryBuilder? summary = null, Func<bool>? summaryEnabled = null, bool refreshSchemaWhenUnknown = false, Func<DateTimeOffset>? clock = null, IReadOnlyList<string>? exampleQuestions = null, IReadOnlyList<string>? exampleQuestionsTurkish = null,
        CrmNlQuery.Application.Drafts.Services.DraftService? draft = null, Func<bool>? draftEnabled = null, CrmNlQuery.Application.Glossary.Services.GlossaryService? glossary = null)
    {
        _glossary = glossary;
        _draft = draft;
        _draftEnabled = draftEnabled ?? (() => true);
        _exampleQuestionsGeneral = exampleQuestions ?? Array.Empty<string>();
        _exampleQuestionsTurkish = exampleQuestionsTurkish ?? Array.Empty<string>();
        _refreshSchemaWhenUnknown = refreshSchemaWhenUnknown;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _summary = summary;
        _summaryEnabled = summaryEnabled ?? (() => true);
        _analysisCache = analysisCache;
        _stepFiller = stepFiller;
        _stepIdColumn = stepIdColumn;
        _metadata = metadata;
        _analyzer = analyzer;
        _searcher = searcher;
        _columns = columns;
        _planner = planner;
        _executor = executor;
        _decomposer = decomposer;
        _knowledge = knowledge;
        _limits = limits ?? FixedLimits.Default;
        _logger = logger ?? NullLogger<AskPipeline>.Instance;
    }

    /// <summary>
    /// Runs a question through the pipeline and returns the outcome.
    /// </summary>
    public async Task<AskOutcome> RunAsync(AskRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
        {
            return new FailureOutcome(MessageCatalog.Now(Msg.EmptyQuestion), "empty_question");
        }
        var maxChars = _limits.Current.MaxQuestionChars;
        if (request.Question.Length > maxChars)
        {
            return new FailureOutcome(UserMessages.QuestionTooLong(maxChars), "question_too_long");
        }
        if (request.Previous is not null && await PreviousQueryGuard.FilterAsync(request.Previous, _metadata, ct) is null)
        {
            request = request with { Previous = null, ForceContinue = false };
        }
        try
        {
            return await RunStepsAsync(request, ct);
        }
        catch (LlmUnavailableException ex)
        {
            LogProblem(LogLevel.Warning, "llm_unavailable", ex);
            return FailureOutcome.FromKey(CrmNlQuery.Application.Localization.Models.Msg.ModelUnavailable, "llm_unavailable");
        }
        catch (PromptTooLongException ex)
        {
            _logger.LogWarning(ex, "Request {RequestId} ended with context_overflow ({Cause}): prompt {PromptTokens}, output {OutputTokens}, window {Window}",
                RequestContext.Current, ex.Cause, ex.PromptTokens, ex.OutputTokens, ex.ContextTokens);
            return FailureOutcome.FromKey(CrmNlQuery.Application.Localization.Models.Msg.ContextOverflow, "context_overflow");
        }
        catch (BudgetExceededException ex)
        {
            LogProblem(LogLevel.Warning, "budget_exceeded", ex);
            return new FailureOutcome(UserMessages.BudgetExceeded, "budget_exceeded");
        }
        catch (LlmOutputException ex)
        {
            LogProblem(LogLevel.Warning, "llm_output", ex);
            return new FailureOutcome(UserMessages.WithExamples(UserMessages.ModelOutput, Examples()), "llm_output");
        }
        catch (CrmException ex) when (ex.IsPermissionDenied)
        {
            LogProblem(LogLevel.Information, "permission", ex);
            return new FailureOutcome(ex.Message, "permission");
        }
        catch (CrmException ex) when (ex.IsAggregateLimit)
        {
            LogProblem(LogLevel.Information, "aggregate_limit", ex);
            return new FailureOutcome(UserMessages.AggregateTooBig, "aggregate_limit");
        }
        catch (CrmException ex) when (ex.IsImpersonationDenied)
        {
            LogProblem(LogLevel.Error, "impersonation", ex);
            return new FailureOutcome(ex.Message, "impersonation");
        }
        catch (CrmException ex)
        {
            LogProblem(LogLevel.Warning, "crm_error", ex);
            return FailureOutcome.FromKey(CrmNlQuery.Application.Localization.Models.Msg.CrmUnavailable, "crm_error");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogProblem(LogLevel.Error, "unexpected", ex);
            return new FailureOutcome(MessageCatalog.Now(Msg.Unexpected, MessageCatalog.Args(("type", ex.GetType().Name))), "unexpected");
        }
    }

    private void LogProblem(LogLevel level, string kind, Exception ex)
    {
        _logger.Log(level, ex, "Request {RequestId} ended with {Kind} ({ExceptionType})", RequestContext.Current, kind, ex.GetType().Name);
    }

    private async Task<AskOutcome> RunStepsAsync(AskRequest request, CancellationToken ct)
    {
        var trigger = _decomposer is null ? StepTrigger.None : ConditionalQuestionFilter.Classify(request.Question);
        Understanding? understood = null;
        IReadOnlyList<QuestionStep>? steps = null;
        if (trigger == StepTrigger.OnlyRanking)
        {
            understood = await UnderstandAsync(request, ct);
        }
        if (trigger == StepTrigger.Always || (understood is not null && StepRouter.NeedsDecomposer(request.Question, understood.Analysis)))
        {
            var found = await _decomposer!.DecomposeAsync(request.Question, ct);
            steps = found.Count > 1 ? found : null;
        }

        if (steps is null)
        {
            var single = await RunCoreAsync(request, null, ct, understood);
            if (single is not FailureOutcome { Kind: NeedsStepsKind })
            {
                return single;
            }
            if (_decomposer is not null)
            {
                var fallback = await _decomposer.DecomposeAsync(request.Question, ct);
                if (fallback.Count > 1)
                {
                    return await RunChainAsync(request, fallback, ct);
                }
            }
            return new FailureOutcome(UserMessages.NeedsSteps, NeedsStepsKind);
        }
        return await RunChainAsync(request, steps, ct);
    }

    private sealed record StepRun(int Number, int? UsesStep, IReadOnlyList<string> IdEntities, bool ProducesIds, IReadOnlyList<string>? InputIds);

    private async Task<AskOutcome> RunChainAsync(AskRequest request, IReadOnlyList<QuestionStep> steps, CancellationToken ct)
    {
        var notes = new List<string>();
        var executed = new List<(int Number, string Question, AnswerOutcome Answer)>();
        var ids = new Dictionary<int, IReadOnlyList<string>>();
        var idEntities = new Dictionary<int, IReadOnlyList<string>>();
        var carryIds = _stepFiller is not null && _stepIdColumn is not null;
        AnswerOutcome? last = null;
        for (var i = 0; i < steps.Count; i++)
        {
            var number = i + 1;
            RequestContext.SetChainStep(number);
            var step = steps[i];
            if (last is not null && !ShouldRun(step.Condition, last.Table.Rows.Count))
            {
                notes.Add(SkippedNote(number, step.Condition));
                continue;
            }

            int? uses = carryIds ? step.UsesStep : null;
            IReadOnlyList<string>? inputIds = null;
            if (uses is int usedStep)
            {
                inputIds = ids.TryGetValue(usedStep, out var found) ? found : new List<string>();
                if (inputIds.Count == 0)
                {
                    notes.Add(MessageCatalog.Now(Msg.NoteStepNotRun, MessageCatalog.Args(("number", number.ToString()), ("used", usedStep.ToString()))));
                    break;
                }
                if (inputIds.Count > StepInputFiller.MaxIds)
                {
                    return new FailureOutcome(UserMessages.TooManyIds(usedStep, inputIds.Count), "too_many_ids");
                }
            }
            var producesIds = carryIds && steps.Skip(i + 1).Any(s => s.UsesStep == number);
            var run = uses is not null || producesIds
                ? new StepRun(number, uses, uses is int u && idEntities.TryGetValue(u, out var entitiesOfUsed) ? entitiesOfUsed : new List<string>(), producesIds, inputIds)
                : null;

            var outcome = await RunCoreAsync(StepRequest(request, step.Question, number), run, ct, inChain: true);
            switch (outcome)
            {
                case FailureOutcome failure:
                    return new FailureOutcome(MessageCatalog.Now(Msg.StepFailed, MessageCatalog.Args(("step", number.ToString()), ("message", failure.Message))), failure.Kind);
                case QuestionOutcome question:
                    return question.AnswerKey == "" ? question : question with { AnswerKey = $"s{number}:{question.AnswerKey}" };
                case AnswerOutcome answer:
                    if (answer.Table.Rows.Count == 0 && number < steps.Count)
                    {
                        notes.Add(MessageCatalog.Now(Msg.NoteStepNoRows, MessageCatalog.Args(("number", number.ToString()), ("question", step.Question))));
                    }
                    notes.AddRange(answer.StepNotes);
                    if (producesIds)
                    {
                        ids[number] = answer.Table.RowIds ?? new List<string>();
                        idEntities[number] = await _stepIdColumn!.IdEntitiesAsync(answer.FetchXml, ct);
                    }
                    executed.Add((number, step.Question, answer));
                    last = answer;
                    break;
            }
        }

        var chained = carryIds && steps.Any(s => s.UsesStep is not null);
        var summaries = new List<StepSummary>();
        if (chained)
        {
            foreach (var (number, question, answer) in executed.Take(Math.Max(0, executed.Count - 1)))
            {
                var names = ids.TryGetValue(number, out var stepIds) && idEntities.TryGetValue(number, out var stepEntities)
                    ? await NamesAsync(stepIds, stepEntities, request.Caller, ct)
                    : null;
                summaries.Add(new StepSummary(number, question, answer.Table.Rows.Count, answer.Table.Columns, answer.Table.Rows.Take(SummaryRows).ToList(), names));
            }
        }
        return last! with
        {
            StepNotes = notes,
            StepSummaries = chained ? summaries : Array.Empty<StepSummary>(),
            FromChain = chained,
            NarrowHints = chained ? Array.Empty<NarrowHint>() : last.NarrowHints,
        };
    }

    private const int SummaryRows = 5;

    private async Task<IReadOnlyList<string>?> NamesAsync(IReadOnlyList<string> ids, IReadOnlyList<string> entities, Caller caller, CancellationToken ct)
    {
        var wanted = ids.Where(i => Guid.TryParse(i, out _)).Take(SummaryRows).ToList();
        if (wanted.Count == 0)
        {
            return null;
        }
        foreach (var entityName in entities)
        {
            var info = await _metadata.GetEntityAsync(entityName, ct);
            if (info is null)
            {
                continue;
            }
            var condition = new XElement("condition", new XAttribute("attribute", info.PrimaryIdAttribute), new XAttribute("operator", "in"));
            foreach (var id in wanted)
            {
                condition.Add(new XElement("value", id));
            }
            var fetch = new XElement("fetch", new XAttribute("top", SummaryRows),
                new XElement("entity", new XAttribute("name", info.LogicalName),
                    new XElement("attribute", new XAttribute("name", info.PrimaryNameAttribute)),
                    new XElement("attribute", new XAttribute("name", info.PrimaryIdAttribute), new XAttribute("alias", DistinctRewriter.RowIdAlias)),
                    new XElement("filter", condition)));
            ResultTable table;
            try
            {
                table = await _executor.ExecuteAsync(info.EntitySetName, fetch.ToString(SaveOptions.DisableFormatting), caller, ct);
            }
            catch (CrmException)
            {
                continue;
            }
            if (table.Rows.Count == 0 || table.RowIds is null)
            {
                continue;
            }
            var byId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < table.Rows.Count; i++)
            {
                byId[table.RowIds[i]] = table.Rows[i].Count > 0 ? table.Rows[i][0] : "";
            }
            return wanted.Select(id => byId.TryGetValue(id, out var name) ? name : "").ToList();
        }
        return null;
    }

    private static bool ShouldRun(StepCondition condition, int previousRows)
    {
        return condition switch
        {
            StepCondition.IfPreviousEmpty => previousRows == 0,
            StepCondition.IfPreviousNotEmpty => previousRows > 0,
            _ => true,
        };
    }

    private static string SkippedNote(int number, StepCondition condition)
    {
        return condition == StepCondition.IfPreviousEmpty
            ? MessageCatalog.Now(Msg.NoteSkippedHadRows, MessageCatalog.Args(("number", number.ToString())))
            : MessageCatalog.Now(Msg.NoteSkippedNoRows, MessageCatalog.Args(("number", number.ToString())));
    }

    private static AskRequest StepRequest(AskRequest request, string question, int number)
    {
        var prefix = $"s{number}:";
        var answers = request.Answers
            .Where(a => a.Key.StartsWith(prefix, StringComparison.Ordinal))
            .ToDictionary(a => a.Key.Substring(prefix.Length), a => a.Value);
        return new AskRequest(question, answers, request.Caller);
    }

    private sealed record Understanding(IReadOnlyList<EntitySummary> Entities, IReadOnlyList<string> Knowledge, QuestionAnalysis Analysis, IReadOnlyList<CrmNlQuery.Domain.Glossary.Entities.GlossaryHit> Glossary);

    private async Task<Understanding> UnderstandAsync(AskRequest request, CancellationToken ct)
    {
        var entities = await _metadata.ListEntitiesAsync(ct);
        var previous = request.Previous;
        var searchText = previous is null ? request.Question : previous.Question + " " + request.Question;
        IReadOnlyList<string> knowledge = _knowledge is null ? new List<string>() : await _knowledge.FindAsync(searchText, ct);
        IReadOnlyList<CrmNlQuery.Domain.Glossary.Entities.GlossaryHit> glossaryHits = _glossary is null ? Array.Empty<CrmNlQuery.Domain.Glossary.Entities.GlossaryHit>() : await _glossary.MatchAsync(request.Question, ct);
        string? cacheKey = _analysisCache is null ? null : AnalysisCache.KeyFor(request.Caller, request.Question, entities, knowledge, previous, request.CurrentRecord, glossaryHits);
        if (cacheKey is not null && _analysisCache!.TryGet(cacheKey, out var cached))
        {
            return new Understanding(entities, knowledge, cached, glossaryHits);
        }
        var analysis = await _analyzer.AnalyzeAsync(request.Question, entities, knowledge, previous, request.CurrentRecord, glossaryHits, ct);
        if (cacheKey is not null)
        {
            _analysisCache!.Put(cacheKey, analysis);
        }
        return new Understanding(entities, knowledge, analysis, glossaryHits);
    }

    private bool TryStartSchemaRefresh(string primaryEntity, out string entity)
    {
        entity = primaryEntity ?? "";
        lock (_schemaGate)
        {
            var now = _clock();
            if (now - _lastSchemaRefresh < SchemaRefreshGap)
            {
                return false;
            }
            _lastSchemaRefresh = now;
            return true;
        }
    }

    private async Task<AskOutcome> RunCoreAsync(AskRequest request, StepRun? stepRun, CancellationToken ct, Understanding? known = null, bool inChain = false)
    {
        var understood = known ?? await UnderstandAsync(request, ct);
        if (known is null && _refreshSchemaWhenUnknown && TryStartSchemaRefresh(understood.Analysis.PrimaryEntity, out var unknownEntity) && await _metadata.GetEntityAsync(unknownEntity, ct) is null)
        {
            await _metadata.RefreshAsync(ct);
            understood = await UnderstandAsync(request, ct);
        }
        var knowledge = understood.Knowledge;
        var analysis = understood.Analysis;
        var previous = request.Previous;
        var follow = previous is not null && (request.ForceContinue || analysis.ReferencesPrevious);

        var primaryName = analysis.PrimaryEntity;
        if (_draft is not null && !inChain && stepRun is null && !follow && !CrmNlQuery.Application.Summary.Services.SummaryIntent.IsSummarizable(primaryName) && CrmNlQuery.Application.Drafts.Models.DraftIntent.Matches(request.Question) && _draftEnabled())
        {
            var named = analysis.Mentions.Where(m => CrmNlQuery.Application.Summary.Services.SummaryIntent.IsSummarizable(m.Entity)).Select(m => m.Entity.ToLowerInvariant()).Distinct().ToList();
            if (named.Count == 1)
            {
                primaryName = named[0];
            }
        }
        var primary = await _metadata.GetEntityAsync(primaryName, ct);
        if (primary is null)
        {
            return new QuestionOutcome("entity", "",
                UserMessages.WithExamples(UserMessages.WhichKindOfRecord(understood.Entities), Examples()),
                new List<Choice>(), true);
        }

        var entitiesForPlan = new List<EntityInfo> { primary };
        foreach (var name in analysis.RelatedEntities)
        {
            var related = await _metadata.GetEntityAsync(name, ct);
            if (related is not null && related.LogicalName != primary.LogicalName)
            {
                entitiesForPlan.Add(related);
            }
        }

        if (follow && !entitiesForPlan.Any(e => string.Equals(e.LogicalName, previous!.Entity, StringComparison.OrdinalIgnoreCase)))
        {
            var previousEntity = await _metadata.GetEntityAsync(previous!.Entity, ct);
            if (previousEntity is not null)
            {
                entitiesForPlan.Add(previousEntity);
            }
        }

        var resolved = new List<ResolvedRecord>();
        var aboutCurrent = request.CurrentRecord is not null && analysis.ReferencesCurrentRecord ? request.CurrentRecord : null;
        if (aboutCurrent is not null)
        {
            resolved.Add(aboutCurrent);
            if (!entitiesForPlan.Any(e => string.Equals(e.LogicalName, aboutCurrent.Entity, StringComparison.OrdinalIgnoreCase)))
            {
                var currentEntity = await _metadata.GetEntityAsync(aboutCurrent.Entity, ct);
                if (currentEntity is not null)
                {
                    entitiesForPlan.Add(currentEntity);
                }
            }
        }
        foreach (var mention in analysis.Mentions)
        {
            var step = await ResolveAsync(mention, request, ct);
            if (step.Outcome is not null)
            {
                return step.Outcome;
            }
            resolved.Add(step.Record!);
        }

        var targets = resolved.GroupBy(r => r.Id).Select(g => g.First()).ToList();
        if (_summary is not null && !inChain && stepRun is null && !follow && targets.Count == 1 && CrmNlQuery.Application.Summary.Services.SummaryIntent.IsSummarizable(targets[0].Entity)
            && CrmNlQuery.Application.Summary.Services.SummaryIntent.Matches(request.Question) && _summaryEnabled())
        {
            var target = targets[0];
            using var summaryScope = Observability.AccessLog.AccessLogContext.Begin("summary", RequestContext.Current);
            RequestContext.SetStage("summary");
            var summary = await _summary.BuildAsync(request.Caller, target.Entity, target.Id, target.Name, ct);
            return aboutCurrent is not null && aboutCurrent.Id == target.Id ? summary with { AboutRecord = target.Name } : summary;
        }

        if (_draft is not null && !inChain && stepRun is null && !follow && CrmNlQuery.Application.Drafts.Models.DraftIntent.Matches(request.Question) && _draftEnabled()
            && !(targets.Count == 1 && CrmNlQuery.Application.Summary.Services.SummaryIntent.IsSummarizable(targets[0].Entity)) && !CrmNlQuery.Application.Summary.Services.SummaryIntent.Matches(request.Question))
        {
            return new FailureOutcome(UserMessages.DraftNeedsRecord, "draft_target");
        }

        if (_draft is not null && !inChain && stepRun is null && !follow && targets.Count == 1 && CrmNlQuery.Application.Summary.Services.SummaryIntent.IsSummarizable(targets[0].Entity)
            && CrmNlQuery.Application.Drafts.Models.DraftIntent.Matches(request.Question) && _draftEnabled())
        {
            var target = targets[0];
            using var draftScope = Observability.AccessLog.AccessLogContext.Begin("draft", RequestContext.Current);
            RequestContext.SetStage("draft");
            var draft = await _draft.BuildAsync(request.Caller, target.Entity, target.Id, target.Name, request.Question, ct);
            return draft is DraftOutcome drafted && aboutCurrent is not null && aboutCurrent.Id == target.Id ? drafted with { AboutRecord = target.Name } : draft;
        }

        IReadOnlyList<string> columns = new List<string>();
        if (!analysis.Aggregate && stepRun is { ProducesIds: true })
        {
            columns = new List<string> { primary.PrimaryNameAttribute };
        }
        else if (!analysis.Aggregate)
        {
            request.Answers.TryGetValue("columns", out var columnsAnswer);
            if (columnsAnswer is null && follow && analysis.Columns.Count == 0
                && string.Equals(primary.LogicalName, previous!.Entity, StringComparison.OrdinalIgnoreCase))
            {
                var inherited = FetchXmlColumns.Of(previous.FetchXml);
                if (inherited.Count > 0)
                {
                    columnsAnswer = "custom:" + string.Join(", ", inherited);
                }
            }
            var decision = await _columns.DecideAsync(analysis, columnsAnswer, primary, ct);
            if (decision.Question is not null)
            {
                return decision.Question;
            }
            columns = decision.Columns!;
        }

        var plan = await _planner.GenerateAsync(new PlanRequest(request.Question, entitiesForPlan, resolved, columns, analysis.Aggregate, knowledge, follow ? previous : null,
            stepRun is null ? null : new StepPlanInfo(stepRun.UsesStep, stepRun.IdEntities, stepRun.ProducesIds), understood.Glossary), ct);
        if (plan.NeedsSteps)
        {
            return new FailureOutcome(UserMessages.NeedsSteps, NeedsStepsKind);
        }
        if (plan.CannotAnswer)
        {
            return new FailureOutcome(UserMessages.WithExamples(UserMessages.CannotAnswer, Examples()), CannotAnswerKind);
        }
        if (!plan.Success)
        {
            if (plan.Errors.Any(e => e.Contains(CrmNlQuery.Application.Glossary.Services.GlossaryEnforcer.Marker, StringComparison.Ordinal)))
            {
                await RecordGlossaryAsync(plan.Glossary, ct);
                var terms = string.Join(", ", plan.Glossary.Where(f => !f.Applied).Select(f => f.Hit.Phrase).Distinct());
                return new FailureOutcome(MessageCatalog.Now(Msg.GlossaryNotApplied, MessageCatalog.Args(("term", terms))), "glossary_not_applied");
            }
            if (follow && plan.Errors.Any(e => e.Contains("at most", StringComparison.Ordinal)))
            {
                return new FailureOutcome(UserMessages.ChainTooComplex, "chain_too_complex");
            }
            if (plan.Errors.Any(e => e.Contains(LlmPlanGenerator.UnknownRecordMarker, StringComparison.Ordinal)))
            {
                return new FailureOutcome(UserMessages.UnknownRecord, "unknown_record");
            }
            return new FailureOutcome(MessageCatalog.Now(Msg.InvalidPlan, MessageCatalog.Args(("errors", string.Join("; ", plan.Errors)))), "invalid_plan");
        }

        var fetch = plan.FetchXml!;
        if (stepRun?.UsesStep is int usedStep && stepRun.InputIds is not null)
        {
            var filled = await _stepFiller!.FillAsync(fetch, new Dictionary<int, IReadOnlyList<string>> { [usedStep] = stepRun.InputIds }, ct);
            if (filled.Status != FillStatus.Filled)
            {
                return new FailureOutcome(MessageCatalog.Now(Msg.InvalidPlan, MessageCatalog.Args(("errors", string.Join("; ", filled.Errors)))), "invalid_plan");
            }
            fetch = filled.FetchXml!;
        }

        TieHandling.TiePlan? tie = null;
        if (stepRun is { ProducesIds: true })
        {
            fetch = await _stepIdColumn!.EnsureAsync(fetch, ct);
            tie = TieHandling.Prepare(fetch, assumeTopOne: true);
        }

        var table = await _executor.ExecuteAsync(primary.EntitySetName, tie?.FetchXml ?? fetch, request.Caller, ct);
        var notes = new List<string>();
        if (tie is not null)
        {
            var resolvedTie = TieHandling.Resolve(tie, table);
            table = resolvedTie.Table;
            if (resolvedTie.Note is not null)
            {
                notes.Add(resolvedTie.Note);
            }
        }
        var truncated = IsTruncated(fetch, table.Rows.Count);
        await RecordGlossaryAsync(plan.Glossary, ct);
        var glossaryApplied = await DescribeGlossaryAsync(plan.Glossary, ct);
        return new AnswerOutcome(primary.LogicalName, Understood(primary, resolved, table), fetch, table, truncated)
        {
            BasedOn = follow ? previous!.Question : null,
            NarrowHints = truncated && stepRun is null ? NarrowHintBuilder.For(fetch, primary) : Array.Empty<NarrowHint>(),
            AboutRecord = aboutCurrent?.Name,
            StepNotes = notes,
            ResolvedRecords = resolved.ToList(),
            GlossaryApplied = glossaryApplied,
        };
    }

    private async Task RecordGlossaryAsync(IReadOnlyList<CrmNlQuery.Domain.Glossary.Entities.GlossaryFinding> findings, CancellationToken ct)
    {
        if (_glossary is not null && findings.Count > 0)
        {
            await _glossary.RecordAsync(findings, ct);
        }
    }

    private async Task<IReadOnlyList<GlossaryApplied>> DescribeGlossaryAsync(IReadOnlyList<CrmNlQuery.Domain.Glossary.Entities.GlossaryFinding> findings, CancellationToken ct)
    {
        var list = new List<GlossaryApplied>();
        foreach (var f in findings.Where(x => x.Applied))
        {
            var info = await _metadata.GetEntityAsync(f.Hit.Entry.Entity, ct);
            list.Add(new GlossaryApplied(f.Hit.Phrase, f.Hit.Entry.Kind, CrmNlQuery.Application.Glossary.Services.GlossaryExplain.Describe(f.Hit.Entry, info)));
        }
        return list;
    }

    /// <summary>
    /// Decides truncation from the validated final query and row count, not the model's analysis; the result page uses the same rule.
    /// </summary>
    public static bool IsTruncated(string fetchXml, int rowCount)
    {
        return !IsAggregateFetch(fetchXml) && rowCount >= PlanValidator.MaxRows;
    }

    private static bool IsAggregateFetch(string fetchXml)
    {
        var value = XDocument.Parse(fetchXml).Root!.Attribute("aggregate")?.Value;
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record ResolveStep(ResolvedRecord? Record, AskOutcome? Outcome);

    private static string? AlternativeFor(string logicalName)
    {
        return logicalName switch
        {
            "account" => "contact",
            "contact" => "account",
            _ => null,
        };
    }

    private async Task<ResolveStep> ResolveAsync(Mention mention, AskRequest request, CancellationToken ct)
    {
        var key = "resolve:" + mention.Text;
        var alternativeKey = "resolve-alt:" + mention.Text;
        var mentionEntity = await _metadata.GetEntityAsync(mention.Entity, ct);
        if (mentionEntity is null)
        {
            return new ResolveStep(null, new FailureOutcome(MessageCatalog.Now(Msg.UnknownEntity, MessageCatalog.Args(("text", mention.Text))), "unknown_entity"));
        }
        var alternativeName = AlternativeFor(mentionEntity.LogicalName);

        if (request.Answers.TryGetValue(key, out var chosen) && TryParseChoice(chosen, out var id, out var name))
        {
            return new ResolveStep(new ResolvedRecord(mention.Text, mentionEntity.LogicalName, id, name), null);
        }
        if (alternativeName is not null && request.Answers.TryGetValue(alternativeKey, out var chosenAlternative) && TryParseChoice(chosenAlternative, out var alternativeId, out var alternativeChosenName))
        {
            return new ResolveStep(new ResolvedRecord(mention.Text, alternativeName, alternativeId, alternativeChosenName), null);
        }

        var matches = await _searcher.SearchByNameAsync(mentionEntity, mention.Text, request.Caller, ct);
        if (matches.Count == 1)
        {
            return new ResolveStep(new ResolvedRecord(mention.Text, mentionEntity.LogicalName, matches[0].Id, matches[0].Name), null);
        }
        if (matches.Count > 1)
        {
            return new ResolveStep(null, ChoiceQuestion(key, MessageCatalog.Now(Msg.ClarWhichRecord, MessageCatalog.Args(("label", mentionEntity.Label), ("text", mention.Text))), matches));
        }

        EntityInfo? alternative = null;
        if (alternativeName is not null)
        {
            alternative = await _metadata.GetEntityAsync(alternativeName, ct);
            if (alternative is not null)
            {
                IReadOnlyList<RecordMatch> alternativeMatches = Array.Empty<RecordMatch>();
                try
                {
                    alternativeMatches = await _searcher.SearchByNameAsync(alternative, mention.Text, request.Caller, ct);
                }
                catch (CrmException ex) when (ex.IsPermissionDenied)
                {
                    alternative = null;
                }
                if (alternativeMatches.Count == 1)
                {
                    return new ResolveStep(new ResolvedRecord(mention.Text, alternative!.LogicalName, alternativeMatches[0].Id, alternativeMatches[0].Name), null);
                }
                if (alternativeMatches.Count > 1)
                {
                    return new ResolveStep(null, ChoiceQuestion(alternativeKey, MessageCatalog.Now(Msg.ClarWhichRecord, MessageCatalog.Args(("label", alternative!.Label), ("text", mention.Text))), alternativeMatches));
                }
            }
        }

        var firstWord = mention.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (firstWord is not null && firstWord != mention.Text)
        {
            var similar = await _searcher.SearchByNameAsync(mentionEntity, firstWord, request.Caller, ct);
            if (similar.Count > 0)
            {
                return new ResolveStep(null, ChoiceQuestion(key, MessageCatalog.Now(Msg.ClarFoundNoNamed, MessageCatalog.Args(("label", mentionEntity.Label), ("text", mention.Text))), similar));
            }
        }
        var searchedLabels = alternative is null ? mentionEntity.Label : mentionEntity.Label + MessageCatalog.Now(Msg.Or) + alternative.Label;
        return new ResolveStep(null, new FailureOutcome(MessageCatalog.Now(Msg.NotFound, MessageCatalog.Args(("labels", searchedLabels), ("text", mention.Text))), "not_found"));
    }

    private IReadOnlyList<string> Examples() => ExampleQuestions.For(RequestLanguage.Current, _exampleQuestionsGeneral, _exampleQuestionsTurkish);

    private static QuestionOutcome ChoiceQuestion(string key, string text, IReadOnlyList<RecordMatch> matches)
    {
        var repeated = matches.GroupBy(m => NameNormalizer.Normalize(m.Name)).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        var choices = matches
            .Select(m => new Choice(repeated.Contains(NameNormalizer.Normalize(m.Name)) ? Disambiguated(m) : m.Name, $"{m.Id}::{m.Name}"))
            .ToList();
        return new QuestionOutcome("resolve", key, text, choices, false);
    }

    private static string Disambiguated(RecordMatch m)
    {
        var shortId = "#" + m.Id.ToString("N").Substring(26);
        var detail = m.CreatedOn is null ? shortId : MessageCatalog.Now(Msg.ClarCreatedDetail, MessageCatalog.Args(("date", m.CreatedOn.Value.ToString("yyyy-MM-dd")), ("id", shortId)));
        return $"{m.Name} ({detail})";
    }

    private static bool TryParseChoice(string value, out Guid id, out string name)
    {
        id = Guid.Empty;
        name = "";
        var index = value.IndexOf("::", StringComparison.Ordinal);
        if (index < 0 || !Guid.TryParse(value.Substring(0, index), out id))
        {
            return false;
        }
        name = value.Substring(index + 2);
        return true;
    }

    private static string Understood(EntityInfo primary, List<ResolvedRecord> resolved, ResultTable table)
    {
        var parts = new List<string> { $"Showing {primary.Label} ({primary.LogicalName})" };
        if (resolved.Count > 0)
        {
            parts.Add("matched: " + string.Join(", ", resolved.Select(r => $"{r.Mention} → {r.Name}")));
        }
        parts.Add("columns: " + string.Join(", ", table.Columns));
        return string.Join("; ", parts);
    }
}
