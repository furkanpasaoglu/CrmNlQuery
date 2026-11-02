using System.Text.Json;
using CrmNlQuery.Application.Design.Models;
using CrmNlQuery.Application.Design.Services;

namespace CrmNlQuery.Eval.Suites;

/// <summary>
/// Runs design assistant eval against the real lab schema and a real model.
/// </summary>
public static class DesignEval
{
    private sealed class DesignCase
    {
        public string Id { get; set; } = "";
        public string Request { get; set; } = "";
        public DesignExpect Expect { get; set; } = new DesignExpect();
    }

    private sealed class DesignExpect
    {
        public List<string>? MustFind { get; set; }
        public List<string>? MustUse { get; set; }
        public List<string>? MustNotCreate { get; set; }
        public List<string>? MustNotFind { get; set; }
        public bool MustFlagSensitive { get; set; }
        public int MinOpenQuestions { get; set; } = 2;
        public bool Clean { get; set; } = true;
    }

    /// <summary>
    /// Runs the design cases from a file and writes the outputs.
    /// </summary>
    public static async Task RunAsync(DesignPlanner planner, string path, int repeat, string? onlyCase)
    {
        var cases = JsonSerializer.Deserialize<List<DesignCase>>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var outDir = Path.Combine("eval/results", $"design-{DateTime.Now:yyyyMMdd-HHmmss}");
        Directory.CreateDirectory(outDir);
        var passed = 0;
        var total = 0;
        foreach (var c in cases.Where(c => onlyCase is null || c.Id == onlyCase))
        {
            for (var run = 1; run <= repeat; run++)
            {
                total++;
                var watch = System.Diagnostics.Stopwatch.StartNew();
                DesignResult? result = null;
                string verdict;
                try
                {
                    result = await planner.RunAsync(new DesignRequest(c.Request));
                    verdict = Check(c.Expect, result) ?? "ok";
                }
                catch (Exception ex)
                {
                    verdict = "EXCEPTION " + ex.GetType().Name + ": " + ex.Message;
                }
                var ok = verdict == "ok";
                if (ok) { passed++; }
                Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {c.Id,-32} {verdict}  ({watch.Elapsed.TotalSeconds:0}s)");
                if (result is not null)
                {
                    File.WriteAllText(Path.Combine(outDir, $"{c.Id}-{run}.md"), DesignRenderer.Markdown(result));
                    File.WriteAllText(Path.Combine(outDir, $"{c.Id}-{run}.json"), DesignRenderer.Json(result));
                }
            }
        }
        Console.WriteLine($"\nSummary: {passed}/{total}  (documents in {outDir})");
    }

    private static string? Check(DesignExpect e, DesignResult r)
    {
        if (e.Clean && r.Problems.Count > 0)
        {
            return "design has problems: " + string.Join(" | ", r.Problems.Take(3));
        }
        foreach (var name in e.MustFind ?? new List<string>())
        {
            if (!r.Matches.Any(m => m.Entity == name))
            {
                return $"the schema search did not find '{name}'";
            }
        }
        foreach (var name in e.MustNotFind ?? new List<string>())
        {
            if (r.Matches.Any(m => m.Entity == name))
            {
                return $"the schema search wrongly found '{name}'";
            }
        }
        foreach (var name in e.MustUse ?? new List<string>())
        {
            var decision = r.Decisions.FirstOrDefault(d => d.Entity == name);
            if (decision is null || decision.Action == "ignore")
            {
                return $"'{name}' should be extended or reused but the decision was '{decision?.Action ?? "none"}'";
            }
        }
        foreach (var name in e.MustNotCreate ?? new List<string>())
        {
            if (r.Document.Tables.Any(t => t.LogicalName == name && string.IsNullOrWhiteSpace(t.Extends)))
            {
                return $"'{name}' was created as a new table";
            }
        }
        if (e.MustFlagSensitive && DesignRenderer.SensitiveFields(r.Document).Count == 0)
        {
            return "no sensitive field was flagged";
        }
        if (r.Document.OpenQuestions.Count < e.MinOpenQuestions)
        {
            return $"only {r.Document.OpenQuestions.Count} open question(s)";
        }
        return null;
    }
}
