using System.Text.Json;

namespace CrmNlQuery.Eval.Suites;

/// <summary>
/// Runs the model-free check that stored FetchXML is still allowed by the data policy.
/// </summary>
public static class StoredQueryEval
{
    private sealed class StoredCase
    {
        public string Id { get; set; } = "";
        public string FetchXml { get; set; } = "";
        public bool Usable { get; set; }
    }

    /// <summary>
    /// Runs the stored-query cases with a checker function and reports the outcomes.
    /// </summary>
    public static async Task<int> RunAsync(Func<string, CancellationToken, Task<bool>> check, string path)
    {
        var cases = JsonSerializer.Deserialize<List<StoredCase>>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var pass = 0;
        foreach (var c in cases)
        {
            var usable = await check(c.FetchXml, CancellationToken.None);
            var ok = usable == c.Usable;
            if (ok) { pass++; }
            Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {c.Id}  usable={usable} expected={c.Usable}");
        }
        Console.WriteLine($"Stored-query cases: {pass}/{cases.Count}");
        return pass == cases.Count ? 0 : 1;
    }
}
