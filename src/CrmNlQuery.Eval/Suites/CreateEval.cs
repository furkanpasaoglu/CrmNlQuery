using System.Text.Json;
using CrmNlQuery.Application.Create.Abstractions;
using CrmNlQuery.Application.Create.Services;
using CrmNlQuery.Domain.Create.Entities;

namespace CrmNlQuery.Eval.Suites;

/// <summary>
/// Measures record creation against the real lab schema and a real model, without writing to CRM.
/// </summary>
public static class CreateEval
{
    private sealed class Case
    {
        public string Id { get; set; } = "";
        public string Text { get; set; } = "";
        public List<string> Tables { get; set; } = new List<string> { "account", "contact" };
        public Expect Expect { get; set; } = new Expect();
    }

    private sealed class Expect
    {
        public bool Intent { get; set; } = true;
        public string? Entity { get; set; }
        public Dictionary<string, string>? Values { get; set; }
        public List<string>? Absent { get; set; }
        public List<string>? Missing { get; set; }
        public int? Records { get; set; }
        public bool? Complete { get; set; }
    }

    /// <summary>
    /// Runs the creation cases through the extractor and reports how many match expectations.
    /// </summary>
    public static async Task RunAsync(CreateExtractor extractor, ICreateSchemaSource schemas, string path, int repeat, string? onlyCase)
    {
        var cases = JsonSerializer.Deserialize<List<Case>>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var validator = new CreateValidator((entity, text, ct) => Task.FromResult<IReadOnlyList<CrmNlQuery.Domain.Ask.Entities.RecordMatch>>(Array.Empty<CrmNlQuery.Domain.Ask.Entities.RecordMatch>()));
        var passed = 0;
        var total = 0;
        foreach (var c in cases.Where(c => onlyCase is null || c.Id == onlyCase))
        {
            var loaded = new List<CreateSchema>();
            foreach (var table in c.Tables)
            {
                var schema = await schemas.GetAsync(table);
                if (schema is not null) { loaded.Add(schema); }
            }
            for (var run = 1; run <= repeat; run++)
            {
                total++;
                var watch = System.Diagnostics.Stopwatch.StartNew();
                string verdict;
                try
                {
                    verdict = await CheckAsync(c, loaded, extractor, validator) ?? "ok";
                }
                catch (Exception ex)
                {
                    verdict = "EXCEPTION " + ex.GetType().Name + ": " + ex.Message;
                }
                var ok = verdict == "ok";
                if (ok) { passed++; }
                Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {c.Id,-30} {verdict}  ({watch.Elapsed.TotalSeconds:0.0}s)");
            }
        }
        Console.WriteLine($"\nSummary: {passed}/{total}");
    }

    private static async Task<string?> CheckAsync(Case c, List<CreateSchema> loaded, CreateExtractor extractor, CreateValidator validator)
    {
        if (loaded.Count == 0)
        {
            return "no writable schema could be read for: " + string.Join(", ", c.Tables);
        }
        var intent = CreateIntent.Matches(c.Text, loaded);
        if (intent != c.Expect.Intent)
        {
            return $"intent rule said {intent}, expected {c.Expect.Intent}";
        }
        if (!c.Expect.Intent)
        {
            return null;
        }
        var extraction = await extractor.ExtractAsync(c.Text, loaded, null);
        if (extraction is null)
        {
            return "the model did not return usable JSON";
        }
        var schema = loaded.FirstOrDefault(s => string.Equals(s.Entity, extraction.Entity, StringComparison.OrdinalIgnoreCase));
        if (c.Expect.Entity is not null && schema?.Entity != c.Expect.Entity)
        {
            return $"entity was '{extraction.Entity}', expected '{c.Expect.Entity}'";
        }
        if (schema is null)
        {
            return $"the model chose an unknown table '{extraction.Entity}'";
        }
        var records = extraction.Records.Count == 0 ? new List<IReadOnlyDictionary<string, string>> { new Dictionary<string, string>() } : extraction.Records.ToList();
        if (c.Expect.Records is int count && records.Count != count)
        {
            return $"{records.Count} records, expected {count}";
        }
        var rows = new List<CreateRow>();
        foreach (var raw in records)
        {
            rows.Add(await validator.ValidateAsync(schema, raw));
        }
        foreach (var pair in c.Expect.Values ?? new Dictionary<string, string>())
        {
            var field = schema.FindField(pair.Key);
            var found = field is not null && rows.Any(r => r.Values.Any(v => v.Attribute == field.Name && CreateSchema.Key(v.Display) == CreateSchema.Key(pair.Value)));
            if (!found)
            {
                var got = rows.SelectMany(r => r.Values).Where(v => field is not null && v.Attribute == field.Name).Select(v => v.Display).FirstOrDefault() ?? "(none)";
                var returned = string.Join(", ", records.SelectMany(r => r).Select(p => p.Key + "=" + p.Value));
                return $"{pair.Key} should be '{pair.Value}' but was {got} (model returned: {returned})";
            }
        }
        foreach (var name in c.Expect.Absent ?? new List<string>())
        {
            var field = schema.FindField(name);
            if (rows.Any(r => r.Values.Any(v => field is not null && v.Attribute == field.Name)) || records.Any(r => r.Keys.Any(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase))))
            {
                return $"'{name}' must not be filled";
            }
        }
        foreach (var label in c.Expect.Missing ?? new List<string>())
        {
            var field = schema.FindField(label);
            if (!rows[0].Problems.Any(p => p.Code == "missing_required" && field is not null && p.Attribute == field.Name))
            {
                return $"'{label}' should be reported as missing";
            }
        }
        if (c.Expect.Complete is bool complete && rows[0].IsComplete != complete)
        {
            return complete ? "the first record should be complete: " + string.Join(" | ", rows[0].Problems.Select(p => p.Code + ":" + p.Label)) : "the first record should have problems";
        }
        return null;
    }
}
