using System.Text.Json;
using System.Text.Json.Nodes;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.Quality.Services;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Domain.Metadata.Entities;

namespace CrmNlQuery.Eval.Suites;

/// <summary>
/// Command-line tool that generates a starter acceptance set from the schema by gathering facts from the CRM.
/// </summary>
public static class StarterSetCommand
{
    private static readonly string[] DefaultTables = { "account", "contact", "opportunity", "incident", "product", "contract", "salesorder", "lead" };

    /// <summary>
    /// Gathers facts from the CRM and writes the generated starter acceptance set file.
    /// </summary>
    public static async Task<int> GenerateAsync(IMetadataSource metadata, Func<string, CancellationToken, Task<ResultTable>> run,
        IReadOnlyList<string>? requested, string language, string outPath, int maxEntities, CancellationToken ct)
    {
        var names = requested is { Count: > 0 } ? requested : await CandidateNamesAsync(metadata, ct);
        var infos = new List<EntityInfo>();
        foreach (var name in names)
        {
            var info = await metadata.GetEntityAsync(name, ct);
            if (info is null) { Console.WriteLine($"  skipped {name}: not in the (policy-filtered) schema"); continue; }
            infos.Add(info);
        }

        StarterFactsCollector.RunQuery runQuery = (fetch, token) => run(fetch, token);
        Action<string> log = message => Console.WriteLine("  " + message);
        var counts = new Dictionary<string, int>();
        foreach (var info in infos)
        {
            counts[info.LogicalName] = await StarterFactsCollector.CountRecordsAsync(info, runQuery, log, ct) ?? 0;
        }
        var chosen = infos.Where(i => counts[i.LogicalName] > 0 && !string.IsNullOrEmpty(i.PrimaryNameAttribute))
            .OrderByDescending(i => counts[i.LogicalName])
            .Take(requested is { Count: > 0 } ? requested.Count : maxEntities).ToList();
        Console.WriteLine($"Entities: {string.Join(", ", chosen.Select(c => $"{c.LogicalName} ({counts[c.LogicalName]})"))}");
        var facts = await StarterFactsCollector.CollectAsync(chosen, counts, runQuery, log, ct);

        var cases = StarterSetGenerator.Generate(chosen, facts, language);
        var json = new JsonArray();
        foreach (var c in cases)
        {
            var reference = new JsonObject
            {
                ["fetchXml"] = c.FetchXml,
                ["compare"] = new JsonObject { ["columns"] = c.Columns, ["ties"] = c.Ties },
            };
            var item = new JsonObject
            {
                ["id"] = c.Id,
                ["question"] = c.Question,
            };
            if (c.AnswerColumns is not null) { item["answers"] = new JsonObject { ["columns"] = c.AnswerColumns }; }
            item["expect"] = new JsonObject { ["finalKind"] = "answer", ["entity"] = c.Entity, ["reference"] = reference };
            item["generated"] = true;
            item["verified"] = false;
            item["template"] = c.Template;
            item["note"] = c.Note;
            json.Add(item);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        File.WriteAllText(outPath, json.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        Console.WriteLine($"{cases.Count} case(s) written to {outPath}. They are UNVERIFIED: read them (--list-generated), then --verify-generated.");
        return 0;
    }

    private static async Task<IReadOnlyList<string>> CandidateNamesAsync(IMetadataSource metadata, CancellationToken ct)
    {
        var all = await metadata.ListEntitiesAsync(ct);
        return all.Where(e => DefaultTables.Contains(e.LogicalName) || e.LogicalName.Contains('_')).Select(e => e.LogicalName).ToList();
    }

    /// <summary>
    /// Lists the cases of a generated starter set file.
    /// </summary>
    public static int List(string path)
    {
        var items = JsonNode.Parse(File.ReadAllText(path))!.AsArray();
        Console.WriteLine($"{"id",-34} {"template",-10} {"verified",-9} question");
        foreach (var item in items)
        {
            Console.WriteLine($"{(string?)item!["id"],-34} {(string?)item["template"],-10} {((bool?)item["verified"] == true ? "yes" : "no"),-9} {(string?)item["question"]}");
            var fetch = (string?)item["expect"]?["reference"]?["fetchXml"] ?? "";
            Console.WriteLine($"{"",-55} reference: {(fetch.Length > 150 ? fetch.Substring(0, 150) + "…" : fetch)}");
        }
        Console.WriteLine($"{items.Count} case(s); verified: {items.Count(i => (bool?)i!["verified"] == true)}");
        return 0;
    }

    /// <summary>
    /// Verifies the chosen cases of a generated starter set file by running them.
    /// </summary>
    public static int Verify(string path, string idsText)
    {
        var items = JsonNode.Parse(File.ReadAllText(path))!.AsArray();
        var all = string.Equals(idsText, "all", StringComparison.OrdinalIgnoreCase);
        var wanted = idsText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.Ordinal);
        var changed = 0;
        foreach (var item in items)
        {
            var id = (string?)item!["id"] ?? "";
            if ((all || wanted.Contains(id)) && (bool?)item["verified"] != true) { item["verified"] = true; changed++; }
        }
        File.WriteAllText(path, items.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        Console.WriteLine($"{changed} case(s) marked verified in {path}.");
        return 0;
    }
}
