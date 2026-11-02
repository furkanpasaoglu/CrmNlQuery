using System.Text.Json;
using CrmNlQuery.Application.Ask.Models;
using CrmNlQuery.Application.Ask.Services;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Eval.Models;
using CrmNlQuery.Eval.Services;

namespace CrmNlQuery.Eval.Suites;

/// <summary>
/// Runs follow-up question chains in eval, turn by turn.
/// </summary>
public static class ChainEval
{
    /// <summary>
    /// Runs the chains and returns how many chains passed.
    /// </summary>
    public static async Task<(int ChainsPassed, int Chains)> RunAsync(AskPipeline pipeline, string path, int repeat, bool verbose, Func<string, string, CancellationToken, Task<ResolvedRecord?>> contextFinder, Func<string, CancellationToken, Task<ResultTable>>? referenceRunner = null)
    {
        var chains = JsonSerializer.Deserialize<List<ChainCase>>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var passed = 0;
        foreach (var chain in chains)
        {
            var runsOk = 0;
            var lines = new List<string>();
            for (var run = 1; run <= repeat; run++)
            {
                var (ok, runLines) = await RunOnceAsync(pipeline, chain, verbose, contextFinder, referenceRunner);
                if (ok) { runsOk++; }
                if (!ok || run == 1) { lines = runLines; }
            }
            var verdict = EvalRunner.Verdict(runsOk, repeat);
            if (verdict == "PASS") { passed++; }
            Console.WriteLine($"{verdict,-5} {(repeat > 1 ? $"{runsOk}/{repeat} " : "")}{chain.Id}");
            foreach (var line in lines) { Console.WriteLine("      " + line); }
        }
        Console.WriteLine();
        Console.WriteLine($"Chains: {passed}/{chains.Count} passed");
        return (passed, chains.Count);
    }

    private static async Task<(bool Ok, List<string> Lines)> RunOnceAsync(AskPipeline pipeline, ChainCase chain, bool verbose, Func<string, string, CancellationToken, Task<ResolvedRecord?>> contextFinder, Func<string, CancellationToken, Task<ResultTable>>? referenceRunner)
    {
        var lines = new List<string>();
        var allOk = true;
        PreviousQuery? previous = null;
        for (var i = 0; i < chain.Turns.Count; i++)
        {
            var turn = chain.Turns[i];
            var (first, final) = await EvalRunner.RunCaseAsync(pipeline, turn, CancellationToken.None, previous, await EvalRunner.ResolveContextAsync(turn, contextFinder, CancellationToken.None));
            var (ok, detail) = EvalRunner.Evaluate(turn, final, first);
            if (ok && turn.Expect.Reference is not null && referenceRunner is not null && final is AnswerOutcome referenced)
            {
                var checkedRef = await ReferenceCheck.RunAsync(turn.Expect.Reference, referenced, referenceRunner, CancellationToken.None);
                ok = checkedRef.Pass;
                detail = checkedRef.Detail;
            }
            allOk &= ok;
            var follow = final is AnswerOutcome { BasedOn: not null } ? "  [follow-up]" : "";
            lines.Add($"{(ok ? "ok  " : "FAIL")} turn {i + 1}: {turn.Question}  -> {detail}{follow}");
            if (!ok && verbose && final is AnswerOutcome failed) { lines.Add("     fetchXml: " + failed.FetchXml); }
            if (final is AnswerOutcome answer)
            {
                previous = new PreviousQuery(turn.Question, answer.Entity, answer.FetchXml);
            }
        }
        return (allOk, lines);
    }
}
