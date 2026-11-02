using System.Text.Json;
using CrmNlQuery.Composition;

namespace CrmNlQuery.Eval.Suites;

/// <summary>
/// Measures retrieval quality without model or CRM, using real local embeddings, ranking the expected document by vector, text and fused search.
/// </summary>
public static class RetrievalEval
{
    private sealed record Row(string Id, int Vector, int Text, int Fused, bool? AnswerChunk);

    /// <summary>
    /// Runs the retrieval cases and reports ranks for each method, returning an exit code.
    /// </summary>
    public static async Task<int> RunAsync(EvalComposition.EvalKnowledge knowledge, string casesPath, int candidates)
    {
        var cases = JsonSerializer.Deserialize<List<RetrievalCase>>(File.ReadAllText(casesPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var rows = new List<Row>();
        foreach (var c in cases)
        {
            var trace = await knowledge.TraceAsync(c.Question, candidates);
            bool? answerChunk = null;
            if (c.ExpectText is { Count: > 0 })
            {
                var notes = string.Join("\n", await knowledge.NotesAsync(c.Question));
                answerChunk = c.ExpectText.All(t => notes.Contains(t, StringComparison.OrdinalIgnoreCase));
            }
            var row = new Row(c.Id, RankOf(trace.Vector, c.Expect), RankOf(trace.Text, c.Expect), RankOf(trace.Fused, c.Expect), answerChunk);
            rows.Add(row);
            var mark = row.Fused is > 0 and <= 4 ? "OK  " : row.Fused > 0 ? "LOW " : "MISS";
            string chunk = "";
            if (answerChunk is not null)
            {
                int ChunkRank(IReadOnlyList<string> chunks)
                {
                    var i = chunks.ToList().FindIndex(t => c.ExpectText!.All(x => t.Contains(x, StringComparison.OrdinalIgnoreCase)));
                    return i < 0 ? 0 : i + 1;
                }
                chunk = answerChunk.Value ? "  chunk OK" : $"  CHUNK MISSING ({string.Join(" | ", c.ExpectText!)}) rank in hybrid={Show(ChunkRank(trace.FusedChunks))} vector={Show(ChunkRank(trace.VectorChunks))} text={Show(ChunkRank(trace.TextChunks))}";
            }
            Console.WriteLine($"{mark} {c.Id,-34} hybrid {Show(row.Fused),-3} vector {Show(row.Vector),-3} text {Show(row.Text),-3}{chunk}" +
                (row.Fused is > 0 and <= 4 ? "" : $"  expected: {string.Join(" | ", c.Expect)}  top: {string.Join(" | ", trace.Fused.Take(3))}"));
        }

        Console.WriteLine();
        Console.WriteLine($"Questions: {rows.Count}   (candidates per method: {candidates})");
        Console.WriteLine($"{"",-8} {"Hit@1",7} {"Hit@4",7} {"Hit@8",7} {"MRR",7}");
        Print("hybrid", rows.Select(r => r.Fused).ToList());
        Print("vector", rows.Select(r => r.Vector).ToList());
        Print("text", rows.Select(r => r.Text).ToList());
        var headroom = rows.Count(r => r.Fused > 4);
        var missed = rows.Count(r => r.Fused <= 0);
        Console.WriteLine();
        var withChunk = rows.Where(r => r.AnswerChunk is not null).ToList();
        if (withChunk.Count > 0)
        {
            Console.WriteLine($"Answer-bearing chunk reached the model: {withChunk.Count(r => r.AnswerChunk == true)}/{withChunk.Count}");
        }
        Console.WriteLine($"Reranker headroom (found, but outside the top 4): {headroom - missed + 0} question(s); not found at all: {missed}");
        return 0;
    }

    private static int RankOf(IReadOnlyList<string> titles, List<string> expected)
    {
        for (var i = 0; i < titles.Count; i++)
        {
            if (expected.Any(e => string.Equals(e, titles[i], StringComparison.OrdinalIgnoreCase)))
            {
                return i + 1;
            }
        }
        return 0;
    }

    private static string Show(int rank) => rank == 0 ? "-" : rank.ToString();

    private static void Print(string name, IReadOnlyList<int> ranks)
    {
        double Hit(int k) => 100.0 * ranks.Count(r => r > 0 && r <= k) / Math.Max(1, ranks.Count);
        var mrr = ranks.Sum(r => r > 0 ? 1.0 / r : 0) / Math.Max(1, ranks.Count);
        Console.WriteLine($"{name,-8} {Hit(1),6:F0}% {Hit(4),6:F0}% {Hit(8),6:F0}% {mrr,7:F2}");
    }
}
