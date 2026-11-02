namespace CrmNlQuery.Application.Knowledge.Services;

/// <summary>
/// Combines several ranked lists into one ranking with Reciprocal Rank Fusion.
/// </summary>
public static class RankFusion
{
    public const int K = 60;

    /// <summary>
    /// Fuses ranked lists, most relevant first, so items ranked high in several lists come first.
    /// </summary>
    public static IReadOnlyList<(T Item, double Score)> Fuse<T>(IReadOnlyList<IReadOnlyList<T>> rankedLists) where T : notnull
    {
        var scores = new Dictionary<T, double>();
        var order = new List<T>();
        foreach (var list in rankedLists)
        {
            for (var rank = 0; rank < list.Count; rank++)
            {
                var item = list[rank];
                if (!scores.ContainsKey(item))
                {
                    scores[item] = 0;
                    order.Add(item);
                }
                scores[item] += 1.0 / (K + rank + 1);
            }
        }
        return order
            .Select((item, index) => (Item: item, Score: scores[item], Index: index))
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Index)
            .Select(x => (x.Item, x.Score))
            .ToList();
    }
}
