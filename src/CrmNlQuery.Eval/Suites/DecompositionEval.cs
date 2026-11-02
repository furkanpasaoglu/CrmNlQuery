using System.Text.Json;
using CrmNlQuery.Domain.Ask.Steps;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Domain.Ask.Entities;

namespace CrmNlQuery.Eval.Suites;

/// <summary>
/// Measures only the question decomposer, with no CRM or row data.
/// </summary>
public static class DecompositionEval
{
    private sealed class DecomposeCase
    {
        public string Id { get; set; } = "";
        public string Kind { get; set; } = "";
        public string Question { get; set; } = "";
        public DecomposePlan? Plan { get; set; }
    }

    private sealed class DecomposePlan
    {
        public int Steps { get; set; }
        public List<int?>? DependsOn { get; set; }
    }

    /// <summary>
    /// Runs the decomposer on every case and reports trigger, step count and, if given, router results.
    /// </summary>
    public static async Task RunAsync(IQuestionDecomposer decomposer, string path, int repeat, Func<string, Task<QuestionAnalysis>>? analyze = null)
    {
        var cases = JsonSerializer.Deserialize<List<DecomposeCase>>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var multi = 0; var multiOk = 0; var missed = 0; var wrongSplit = 0;
        var singles = 0; var singleOk = 0; var falseTrigger = 0; var wrongSplitSingle = 0;
        var routerSkippedMulti = 0; var routerSkippedSingle = 0; var routerRanking = 0;

        foreach (var c in cases)
        {
            var isMulti = c.Kind == "multi";
            for (var run = 1; run <= repeat; run++)
            {
                var triggered = ConditionalQuestionFilter.MayNeedSteps(c.Question);
                IReadOnlyList<QuestionStep> steps = new List<QuestionStep> { new QuestionStep(c.Question, StepCondition.Always) };
                var routed = "";
                if (triggered && analyze is not null && ConditionalQuestionFilter.Classify(c.Question) == StepTrigger.OnlyRanking)
                {
                    routerRanking++;
                    var analysis = await analyze(c.Question);
                    if (!StepRouter.NeedsDecomposer(c.Question, analysis))
                    {
                        triggered = false;
                        routed = " [router: decomposer skipped]";
                        if (isMulti) { routerSkippedMulti++; } else { routerSkippedSingle++; }
                    }
                }
                if (triggered)
                {
                    steps = await decomposer.DecomposeAsync(c.Question);
                }
                var shape = string.Join(" ", steps.Select((s, i) => $"[{i + 1}" + (s.UsesStep is int u ? $"<-{u}" : "") + "]"));
                var expectedSteps = c.Plan?.Steps ?? 1;
                var expectedUses = c.Plan?.DependsOn ?? Enumerable.Repeat<int?>(null, expectedSteps).ToList();
                var okShape = steps.Count == expectedSteps && steps.Select(s => s.UsesStep).SequenceEqual(expectedUses);

                string verdict;
                if (isMulti)
                {
                    multi++;
                    if (!triggered) { missed++; verdict = "MISSED (trigger/router did not send it to the decomposer: only the planner's needsSteps flag can still catch it)" + routed; }
                    else if (!okShape) { wrongSplit++; verdict = "WRONG PLAN"; }
                    else { multiOk++; verdict = "ok"; }
                }
                else
                {
                    singles++;
                    if (steps.Count > 1) { wrongSplitSingle++; verdict = "WRONGLY SPLIT"; }
                    else { singleOk++; verdict = triggered ? "ok (false trigger: one extra call)" : "ok"; }
                    if (triggered) { falseTrigger++; }
                }
                Console.WriteLine($"{(verdict.StartsWith("ok") ? "PASS" : "FAIL")}  {c.Id,-42} trigger={(triggered ? "yes" : "no "),-3} steps={shape,-14} {verdict}");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"Multi-step: {multiOk}/{multi} correct plans; missed by the trigger: {missed} (target 0); wrong plans: {wrongSplit}");
        Console.WriteLine($"Single-step traps: {singleOk}/{singles} kept as one step; decomposer calls still made on traps (false triggers): {falseTrigger} (cost: latency only); wrongly split: {wrongSplitSingle}");
        if (analyze is not null)
        {
            Console.WriteLine($"Router: of {routerRanking} ranking-only runs, decomposer skipped for {routerSkippedSingle} single-step traps (saved calls) and for {routerSkippedMulti} multi-step questions (target 0; the planner flag is the safety net)");
        }
    }
}
