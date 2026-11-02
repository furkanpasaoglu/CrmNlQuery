using System.Text.Json;
using CrmNlQuery.Application.Observability.Calls;
using CrmNlQuery.Composition;
using CrmNlQuery.Eval.Suites;
using Microsoft.Extensions.Configuration;
using CrmNlQuery.Application.Ask.Models;
using CrmNlQuery.Eval.Models;
using CrmNlQuery.Eval.Services;

var config = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddEnvironmentVariables()
    .Build();

string casesPath = "eval/cases.json";
string? onlyCase = null;
bool? think = null;
var repeat = 1;
var verbose = false;
var knowledgeOn = false;
var knowledgeDir = "eval/knowledge";
string? retrievalCases = null;
string? chainCases = null;
string? decomposeCases = null;
string? designCases = null;
string? createCases = null;
string? reportCases = null;
var dumpPrompts = false;
var analysisCacheOn = false;
var pruneOn = true;
string? policyPath = null;
string? storedCases = null;
var summaryOn = false;
var draftOn = false;
string? glossaryFile = null;
var candidates = 30;
var generateSet = args.Contains("--generate-set");
string? genEntities = null;
var genLang = "en";
var genMax = 6;
string? genOut = null;
string? listGenerated = null;
string? verifyGenerated = null;
var verifyIds = "all";
var acceptData = args.Contains("--accept-data");
List<int>? loadUsers = null;
var loadRounds = 1;
var targetSimple = 30.0;
var targetMulti = 60.0;
var provider = "ollama";
var models = new List<string>();
for (var i = 0; i < args.Length - 1; i++)
{
    switch (args[i])
    {
        case "--cases": casesPath = args[i + 1]; break;
        case "--case": onlyCase = args[i + 1]; break;
        case "--model": models.Add(args[i + 1]); break;
        case "--think": think = bool.Parse(args[i + 1]); break;
        case "--repeat": repeat = Math.Max(1, int.Parse(args[i + 1])); break;
        case "--provider": provider = args[i + 1].ToLowerInvariant(); break;
        case "--verbose": verbose = bool.Parse(args[i + 1]); break;
        case "--knowledge": knowledgeOn = args[i + 1].ToLowerInvariant() == "on"; break;
        case "--knowledge-dir": knowledgeDir = args[i + 1]; break;
        case "--retrieval-cases": retrievalCases = args[i + 1]; break;
        case "--chain-cases": chainCases = args[i + 1]; break;
        case "--decompose-cases": decomposeCases = args[i + 1]; break;
        case "--design-cases": designCases = args[i + 1]; break;
        case "--create-cases": createCases = args[i + 1]; break;
        case "--report-cases": reportCases = args[i + 1]; break;
        case "--dump-prompts": dumpPrompts = bool.Parse(args[i + 1]); break;
        case "--analysis-cache": analysisCacheOn = args[i + 1].ToLowerInvariant() == "on"; break;
        case "--prune": pruneOn = args[i + 1].ToLowerInvariant() != "off"; break;
        case "--policy": policyPath = args[i + 1]; break;
        case "--stored-cases": storedCases = args[i + 1]; break;
        case "--summary": summaryOn = args[i + 1].ToLowerInvariant() == "on"; break;
        case "--glossary-entries": glossaryFile = args[i + 1]; break;
        case "--draft": draftOn = args[i + 1].ToLowerInvariant() == "on"; break;
        case "--candidates": candidates = Math.Max(1, int.Parse(args[i + 1])); break;
        case "--entities": genEntities = args[i + 1]; break;
        case "--lang": genLang = args[i + 1].ToLowerInvariant(); break;
        case "--max-entities": genMax = Math.Max(1, int.Parse(args[i + 1])); break;
        case "--out": genOut = args[i + 1]; break;
        case "--list-generated": listGenerated = args[i + 1]; break;
        case "--verify-generated": verifyGenerated = args[i + 1]; break;
        case "--ids": verifyIds = args[i + 1]; break;
        case "--users": loadUsers = args[i + 1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(int.Parse).Where(u => u > 0).ToList(); break;
        case "--rounds": loadRounds = Math.Max(1, int.Parse(args[i + 1])); break;
        case "--target-simple": targetSimple = double.Parse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture); break;
        case "--target-multi": targetMulti = double.Parse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture); break;
    }
}
if (listGenerated is not null) { return StarterSetCommand.List(listGenerated); }
if (verifyGenerated is not null) { return StarterSetCommand.Verify(verifyGenerated, verifyIds); }
if (retrievalCases is not null)
{
    var kn = await EvalComposition.CreateKnowledgeAsync(config, KnowledgeFiles.LoadMany(knowledgeDir));
    Console.WriteLine($"Retrieval eval: {kn.Documents} documents from {knowledgeDir}");
    return await RetrievalEval.RunAsync(kn, retrievalCases, candidates);
}
if (provider != "ollama" && provider != "vllm" && provider != "anthropic")
{
    Console.WriteLine($"Unknown --provider '{provider}'. Use ollama, vllm or anthropic.");
    return 1;
}

if (models.Count == 0) { models.Add(EvalComposition.DefaultModel(config, provider)); }

if (provider == "anthropic" && !EvalComposition.HasAnthropicKey(config))
{
    Console.WriteLine("Anthropic API key is missing. In PowerShell: $env:Anthropic__ApiKey = Read-Host \"Anthropic API key\"");
    return 1;
}

var contextFinder = EvalComposition.CreateContextFinder(config);
var referenceRunner = EvalComposition.CreateReferenceRunner(config);
var dataPolicy = policyPath is null ? null : EvalComposition.LoadPolicy(policyPath);

if (generateSet)
{
    if (genOut is null) { Console.WriteLine("--generate-set needs --out <file.json>."); return 1; }
    var (schema, runner) = EvalComposition.CreateSchemaTools(config, dataPolicy);
    var wantedEntities = genEntities?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    return await StarterSetCommand.GenerateAsync(schema, runner, wantedEntities, genLang, genOut, genMax, CancellationToken.None);
}

if (storedCases is not null)
{
    if (dataPolicy is null) { Console.WriteLine("--stored-cases needs --policy <file>."); return 1; }
    return await StoredQueryEval.RunAsync(EvalComposition.CreateStoredQueryChecker(config, dataPolicy), storedCases);
}

if (reportCases is not null)
{
    Console.WriteLine("=== report finder (no model) ===");
    var (reportMetadata, reportRunner) = EvalComposition.CreateSchemaTools(config, null);
    var reportBase = config["Crm:BaseUrl"];
    await ReportEval.RunAsync(reportMetadata, reportRunner, string.IsNullOrEmpty(reportBase) ? "http://crm.example.com/contoso" : reportBase, reportCases, onlyCase);
    return 0;
}

if (createCases is not null)
{
    foreach (var model in models)
    {
        Console.WriteLine($"=== {model} (record creation) ===");
        var (extractor, schemas) = EvalComposition.CreateRecordCreationParts(config, provider, model, think);
        await CreateEval.RunAsync(extractor, schemas, createCases, repeat, onlyCase);
    }
    return 0;
}

if (designCases is not null)
{
    foreach (var model in models)
    {
        Console.WriteLine($"=== {model} (design assistant) ===");
        await DesignEval.RunAsync(EvalComposition.CreateDesigner(config, provider, model, think), designCases, repeat, onlyCase);
    }
    return 0;
}

if (decomposeCases is not null)
{
    foreach (var model in models)
    {
        Console.WriteLine($"=== {model} (decomposition) ===");
        await DecompositionEval.RunAsync(EvalComposition.CreateDecomposer(config, provider, model, think), decomposeCases, repeat, EvalComposition.CreateAnalyzer(config, provider, model, think));
    }
    return 0;
}

if (chainCases is not null)
{
    var chainKnowledge = knowledgeOn ? await EvalComposition.CreateKnowledgeAsync(config, KnowledgeFiles.LoadMany(knowledgeDir)) : null;
    foreach (var model in models)
    {
        var chainPipeline = EvalComposition.CreatePipeline(config, provider, model, think, chainKnowledge?.Source, verbose ? new EvalLogger() : null);
        Console.WriteLine($"=== {model} (chains) ===");
        await ChainEval.RunAsync(chainPipeline, chainCases, repeat, verbose, contextFinder, referenceRunner);
    }
    return 0;
}

var cases = JsonSerializer.Deserialize<List<EvalCase>>(File.ReadAllText(casesPath),
    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
if (onlyCase is not null)
{
    var wanted = onlyCase.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.Ordinal);
    cases = cases.Where(c => wanted.Contains(c.Id)).ToList();
}

EvalComposition.EvalKnowledge? knowledge = null;
if (knowledgeOn)
{
    knowledge = await EvalComposition.CreateKnowledgeAsync(config, KnowledgeFiles.LoadMany(knowledgeDir));
    Console.WriteLine($"Company knowledge ON: {knowledge.Documents} documents from {knowledgeDir}");
}
else
{
    Console.WriteLine("Company knowledge OFF");
}

Directory.CreateDirectory("eval/results");
var summary = new List<(string Model, int Pass, int Total)>();
var storedFingerprints = DataFingerprints.Load(DataFingerprints.DefaultPath);
var freshFingerprints = new Dictionary<string, string>(storedFingerprints);
var dataMissing = new List<string>();
var unverifiedTotal = 0;
var unverifiedPassed = 0;
var dataChanged = new List<string>();
var lowSignal = new List<string>();

foreach (var model in models)
{
    var calls = new LlmCallLog { CaptureText = dumpPrompts };
    var promptDir = Path.Combine("eval/results", $"prompts-{DateTime.Now:yyyyMMdd-HHmmss}");
    var window = EvalComposition.ContextWindow(config);
    var pipeline = EvalComposition.CreatePipeline(config, provider, model, think, knowledge?.Source, verbose ? new EvalLogger() : null, calls, analysisCacheOn, pruneOn, dataPolicy, summaryOn, draftOn, glossaryFile);
    Console.WriteLine($"=== {model} ===");
    var wouldNotFit = new List<string>();

    if (loadUsers is not null && loadUsers.Count > 0)
    {
        Console.WriteLine("Load test: " + string.Join(", ", loadUsers) + " user(s), " + loadRounds + " round(s), " + cases.Count + " case(s) per round");
        await LoadTest.RunAsync(pipeline, cases, loadUsers, loadRounds, calls, model, contextFinder, referenceRunner, targetSimple, targetMulti);
        continue;
    }

    var results = new List<object>();
    var pass = 0;
    var counted = 0;
    foreach (var c in cases)
    {
        var missingRecord = await FindMissingRecordAsync(c, contextFinder);
        if (missingRecord is not null)
        {
            Console.WriteLine($"DATA-MISSING  {c.Id,-30} {missingRecord}");
            dataMissing.Add(c.Id);
            results.Add(new { c.Id, c.Question, ok = false, verdict = "DATA-MISSING", passes = 0, runs = 0, details = new List<string> { missingRecord } });
            continue;
        }
        var unverified = c.Generated == true && c.Verified != true;
        if (unverified) { unverifiedTotal++; } else { counted++; }
        var started = DateTime.UtcNow;
        var callMark = calls.Mark();
        var passes = 0;
        var details = new List<string>();
        for (var run = 1; run <= repeat; run++)
        {
            var (first, final) = await EvalRunner.RunCaseAsync(pipeline, c, CancellationToken.None, null, await EvalRunner.ResolveContextAsync(c, contextFinder, CancellationToken.None));
            var (runOk, runDetail) = EvalRunner.Evaluate(c, final, first);
            if (runOk && c.Expect.Reference is not null && final is AnswerOutcome referenced)
            {
                var checkedRef = await ReferenceCheck.RunAsync(c.Expect.Reference, referenced, referenceRunner, CancellationToken.None);
                runOk = checkedRef.Pass;
                runDetail = checkedRef.Detail;
                if (checkedRef.Detail.Contains("[low-signal", StringComparison.Ordinal) && !lowSignal.Contains(c.Id)) { lowSignal.Add(c.Id); }
                if (checkedRef.Fingerprint != "")
                {
                    if (storedFingerprints.TryGetValue(c.Id, out var before) && before != checkedRef.Fingerprint && !dataChanged.Contains(c.Id))
                    {
                        dataChanged.Add(c.Id);
                        Console.WriteLine($"  DATA-CHANGED {c.Id}: reference rows {DataFingerprints.RowsOf(before)} -> {checkedRef.ReferenceRows} (accepted fingerprint {before}, now {checkedRef.Fingerprint}); the verdict is not affected");
                    }
                    freshFingerprints[c.Id] = checkedRef.Fingerprint;
                }
            }
            if (runOk) { passes++; }
            else if (verbose && final is AnswerOutcome failedAnswer) { Console.WriteLine("    fetchXml of the failed run: " + failedAnswer.FetchXml); }
            details.Add(runDetail);
        }
        var samples = calls.Since(callMark);
        if (dumpPrompts && samples.Count > 0)
        {
            Directory.CreateDirectory(promptDir);
            for (var s = 0; s < samples.Count; s++)
            {
                var promptFile = Path.Combine(promptDir, $"{c.Id}-{s + 1}-{samples[s].Purpose}.txt");
                File.WriteAllText(promptFile, "=== SYSTEM ===\n" + samples[s].System + "\n=== USER ===\n" + samples[s].User + "\n=== REPLY ===\n" + samples[s].Reply + "\n");
            }
        }
        var notFit = LlmCallSummary.WouldNotFit(samples, window);
        if (notFit) { wouldNotFit.Add(c.Id); }
        var titles = knowledge?.TakeFoundTitles();
        var found = titles is null ? "" : "  [knowledge: " + (titles.Count > 0 ? string.Join(" | ", titles) : "none") + "]";
        var verdict = EvalRunner.Verdict(passes, repeat);
        var ok = verdict == "PASS";
        if (ok && unverified) { unverifiedPassed++; }
        else if (ok) { pass++; }
        if (unverified) { verdict = "[unverified] " + verdict; }
        var seconds = (DateTime.UtcNow - started).TotalSeconds;
        if (repeat == 1)
        {
            Console.WriteLine($"{verdict}  {c.Id,-30} {details[0]}  ({seconds:F0}s){found}");
        }
        else
        {
            var shown = verdict == "PASS" ? details[0] : string.Join(" | ", details);
            Console.WriteLine($"{verdict,-5} {passes}/{repeat}  {c.Id,-30} {shown}  ({seconds:F0}s){found}");
        }
        var sizes = new
        {
            calls = samples.Count,
            promptChars = samples.Sum(s => (long)s.PromptChars),
            estTokens = samples.Sum(s => (long)s.EstTokens),
            maxEstTokens = samples.Count == 0 ? 0 : samples.Max(s => s.EstTokens),
            inputTokens = samples.Sum(s => (long)(s.InputTokens ?? 0)),
            outputTokens = samples.Sum(s => (long)(s.OutputTokens ?? 0)),
            modelSeconds = Math.Round(samples.Sum(s => s.Milliseconds) / 1000.0, 1),
            wouldNotFit = notFit,
            overflows = samples.Count(s => s.Overflow is not null),
        };
        results.Add(new { c.Id, c.Question, ok, verdict, passes, runs = repeat, details, sizes });
    }
    summary.Add((model, pass, counted));
    PrintCallTable(calls, window, wouldNotFit);
    var file = Path.Combine("eval/results", $"{model.Replace(':', '_')}{(knowledgeOn ? "-knowledge" : "")}-{DateTime.Now:yyyyMMdd-HHmmss}.json");
    File.WriteAllText(file, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
}

if (acceptData)
{
    DataFingerprints.Save(DataFingerprints.DefaultPath, freshFingerprints);
    Console.WriteLine($"Accepted {freshFingerprints.Count} reference fingerprint(s) -> {DataFingerprints.DefaultPath}");
}
Console.WriteLine();
Console.WriteLine("Summary:");
foreach (var s in summary)
{
    Console.WriteLine(s.Total == 0 && unverifiedTotal > 0
        ? $"  {s.Model,-20} no verified cases (only unverified generated cases ran)"
        : $"  {s.Model,-20} {s.Pass}/{s.Total}  ({100.0 * s.Pass / Math.Max(1, s.Total):F0}%)");
}
if (unverifiedTotal > 0) { Console.WriteLine($"  UNVERIFIED generated cases: {unverifiedPassed}/{unverifiedTotal} passed (not counted in the pass rate; review them, then --verify-generated)"); }
if (dataMissing.Count > 0) { Console.WriteLine($"  DATA-MISSING (not counted): {string.Join(", ", dataMissing)}"); }
if (dataChanged.Count > 0) { Console.WriteLine($"  DATA-CHANGED (verdict unaffected): {string.Join(", ", dataChanged)}  -> review, then --accept-data"); }
if (lowSignal.Count > 0) { Console.WriteLine($"  LOW-SIGNAL references: {string.Join(", ", lowSignal)}"); }
return 0;

static async Task<string?> FindMissingRecordAsync(EvalCase c, Func<string, string, CancellationToken, Task<CrmNlQuery.Domain.Ask.Entities.ResolvedRecord?>> finder)
{
    foreach (var need in c.Requires ?? new List<EvalRequire>())
    {
        var found = await finder(need.Entity, need.Name, CancellationToken.None);
        if (found is null) { return $"required {need.Entity} '{need.Name}' not found in the CRM"; }
    }
    return null;
}

static void PrintCallTable(LlmCallLog calls, int window, List<string> wouldNotFit)
{
    var all = calls.Since(0);
    if (all.Count == 0) { return; }
    Console.WriteLine();
    Console.WriteLine("Model calls by step (prompt size and time):");
    Console.WriteLine($"  {"step",-10} {"calls",6} {"avg est.tok",12} {"max est.tok",12} {"avg in.tok",11} {"avg time",9}");
    foreach (var row in LlmCallSummary.ByPurpose(all))
    {
        Console.WriteLine($"  {row.Purpose,-10} {row.Calls,6} {row.AvgEstTokens,12:F0} {row.MaxEstTokens,12} {row.AvgInputTokens,11:F0} {row.AvgMilliseconds / 1000.0,8:F1}s");
    }
    var totalMs = all.Sum(s => s.Milliseconds) / 1000.0;
    Console.WriteLine($"  total {all.Count} calls, {all.Sum(s => (long)s.PromptChars)} prompt chars, {totalMs:F0}s model time");
    if (window > 0)
    {
        Console.WriteLine(wouldNotFit.Count == 0
            ? $"  Context window {window}: every case fits (by estimate{(all.Any(s => s.Overflow is not null) ? "" : "; no real overflow")})."
            : $"  Context window {window}: {wouldNotFit.Count} case(s) would NOT fit (estimate) or overflowed: {string.Join(", ", wouldNotFit)}");
    }
}
