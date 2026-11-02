using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using CrmNlQuery.Application.Observability.Calls;
using CrmNlQuery.Application.Ask.Models;
using CrmNlQuery.Application.Ask.Services;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Eval.Models;
using CrmNlQuery.Eval.Services;

namespace CrmNlQuery.Eval.Suites;

/// <summary>
/// Eval load test: N virtual users ask questions concurrently, each sequentially, with the same case list for every user count.
/// </summary>
public static class LoadTest
{
    private sealed class Job
    {
        public EvalCase Case { get; set; } = null!;
    }

    private sealed class Measure
    {
        public string Id { get; set; } = "";
        public double Seconds { get; set; }
        public bool Passed { get; set; }
        public bool Multi { get; set; }
    }

    /// <summary>
    /// Runs the load test for each requested user count and number of rounds, measuring times against the simple and multi-step targets.
    /// </summary>
    public static async Task RunAsync(
        AskPipeline pipeline,
        List<EvalCase> cases,
        IReadOnlyList<int> userCounts,
        int rounds,
        LlmCallLog calls,
        string model,
        Func<string, string, CancellationToken, Task<ResolvedRecord?>> contextFinder,
        Func<string, CancellationToken, Task<ResultTable>> referenceRunner,
        double simpleTargetSeconds = 30,
        double multiTargetSeconds = 60)
    {
        if (cases.Count == 0)
        {
            Console.WriteLine("No cases to run.");
            return;
        }

        Console.WriteLine("Warm-up (not measured)...");
        await RunOneAsync(pipeline, cases[0], contextFinder, referenceRunner);

        Console.WriteLine($"Targets (p95 wait per question): simple <= {simpleTargetSeconds:F0}s, multi-step <= {multiTargetSeconds:F0}s");
        var levels = new List<object>();
        var summaryRows = new List<string>();
        var targetRows = new List<string>();
        foreach (var users in userCounts)
        {
            var queue = new ConcurrentQueue<Job>();
            for (var round = 0; round < rounds; round++)
            {
                foreach (var c in cases)
                {
                    queue.Enqueue(new Job { Case = c });
                }
            }
            var total = queue.Count;
            var measures = new ConcurrentBag<Measure>();
            var callMark = calls.Mark();
            Console.WriteLine($"\n--- {users} user(s), {total} questions ---");
            var wall = Stopwatch.StartNew();
            var workers = Enumerable.Range(0, users).Select(_ => Task.Run(async () =>
            {
                while (queue.TryDequeue(out var job))
                {
                    var (seconds, passed) = await RunOneAsync(pipeline, job.Case, contextFinder, referenceRunner);
                    measures.Add(new Measure { Id = job.Case.Id, Seconds = seconds, Passed = passed, Multi = job.Case.MultiStep == true });
                    Console.WriteLine($"  {(passed ? "ok  " : "FAIL")} {job.Case.Id,-34} {seconds,6:F1}s");
                }
            })).ToArray();
            await Task.WhenAll(workers);
            wall.Stop();

            var samples = calls.Since(callMark);
            var outputTokens = samples.Sum(s => (long)(s.OutputTokens ?? 0));
            var times = measures.Select(m => m.Seconds).OrderBy(s => s).ToList();
            var passedCount = measures.Count(m => m.Passed);
            var wallSeconds = wall.Elapsed.TotalSeconds;
            var perMinute = wallSeconds > 0 ? total / (wallSeconds / 60.0) : 0;
            var tokensPerSecond = wallSeconds > 0 ? outputTokens / wallSeconds : 0;
            Console.WriteLine($"  => wall {wallSeconds:F0}s | {perMinute:F1} questions/min | latency p50 {Percentile(times, 0.5):F0}s, p95 {Percentile(times, 0.95):F0}s, max {times.Last():F0}s | passed {passedCount}/{total} | model calls {samples.Count}, output tokens {outputTokens} ({tokensPerSecond:F1}/s)");
            var simpleTimes = measures.Where(m => !m.Multi).Select(m => m.Seconds).OrderBy(s => s).ToList();
            var multiTimes = measures.Where(m => m.Multi).Select(m => m.Seconds).OrderBy(s => s).ToList();
            var simpleP95 = Percentile(simpleTimes, 0.95);
            var multiP95 = Percentile(multiTimes, 0.95);
            var simpleOk = simpleTimes.Count == 0 || simpleP95 <= simpleTargetSeconds;
            var multiOk = multiTimes.Count == 0 || multiP95 <= multiTargetSeconds;
            Console.WriteLine($"  => TARGET: simple p95 {simpleP95:F0}s of {simpleTimes.Count} (<= {simpleTargetSeconds:F0}s) {(simpleOk ? "MET" : "MISSED")} | multi-step p95 {multiP95:F0}s of {multiTimes.Count} (<= {multiTargetSeconds:F0}s) {(multiOk ? "MET" : "MISSED")}");
            targetRows.Add($"  {users,3} | {simpleP95,6:F0} | {(simpleOk ? "MET" : "MISSED"),-6} | {multiP95,6:F0} | {(multiOk ? "MET" : "MISSED"),-6}");
            summaryRows.Add($"  {users,3} | {wallSeconds,7:F0} | {perMinute,6:F1} | {Percentile(times, 0.5),5:F0} | {Percentile(times, 0.95),5:F0} | {times.Last(),5:F0} | {passedCount}/{total}");
            levels.Add(new
            {
                users,
                questions = total,
                wallSeconds = Math.Round(wallSeconds, 1),
                questionsPerMinute = Math.Round(perMinute, 2),
                p50 = Math.Round(Percentile(times, 0.5), 1),
                p95 = Math.Round(Percentile(times, 0.95), 1),
                max = Math.Round(times.Last(), 1),
                passed = passedCount,
                simpleP95 = Math.Round(simpleP95, 1),
                simpleTargetMet = simpleOk,
                multiP95 = Math.Round(multiP95, 1),
                multiTargetMet = multiOk,
                modelCalls = samples.Count,
                outputTokens,
                tokensPerSecond = Math.Round(tokensPerSecond, 1),
                perCase = measures.OrderBy(m => m.Id).Select(m => new { m.Id, seconds = Math.Round(m.Seconds, 1), m.Passed }).ToList(),
            });
        }

        Console.WriteLine("\nSummary (users | wall s | q/min | p50 s | p95 s | max s | passed):");
        foreach (var row in summaryRows)
        {
            Console.WriteLine(row);
        }

        Console.WriteLine("\nTargets (users | simple p95 s | simple | multi p95 s | multi):");
        foreach (var row in targetRows)
        {
            Console.WriteLine(row);
        }

        Directory.CreateDirectory("eval/results");
        var file = Path.Combine("eval/results", $"load-{model.Replace(':', '_')}-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        File.WriteAllText(file, JsonSerializer.Serialize(new { model, rounds, simpleTargetSeconds, multiTargetSeconds, levels }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Result file: {file}");
    }

    private static async Task<(double Seconds, bool Passed)> RunOneAsync(
        AskPipeline pipeline,
        EvalCase c,
        Func<string, string, CancellationToken, Task<ResolvedRecord?>> contextFinder,
        Func<string, CancellationToken, Task<ResultTable>> referenceRunner)
    {
        var current = await EvalRunner.ResolveContextAsync(c, contextFinder, CancellationToken.None);
        var watch = Stopwatch.StartNew();
        AskOutcome first;
        AskOutcome final;
        try
        {
            (first, final) = await EvalRunner.RunCaseAsync(pipeline, c, CancellationToken.None, null, current);
        }
        catch (Exception)
        {
            return (watch.Elapsed.TotalSeconds, false);
        }
        var seconds = watch.Elapsed.TotalSeconds;
        var (ok, _) = EvalRunner.Evaluate(c, final, first);
        if (ok && c.Expect.Reference is not null && final is AnswerOutcome referenced)
        {
            var checkedRef = await ReferenceCheck.RunAsync(c.Expect.Reference, referenced, referenceRunner, CancellationToken.None);
            ok = checkedRef.Pass;
        }
        return (seconds, ok);
    }

    private static double Percentile(List<double> sorted, double p)
    {
        if (sorted.Count == 0)
        {
            return 0;
        }
        var rank = (int)Math.Ceiling(p * sorted.Count);
        return sorted[Math.Clamp(rank - 1, 0, sorted.Count - 1)];
    }
}
