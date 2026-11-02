using CrmNlQuery.Application.Knowledge.Services;

namespace CrmNlQuery.Tests.Application.Knowledge.Services;

/// <summary>
/// Unit tests for reciprocal rank fusion, which merges semantic and keyword rankings for knowledge search.
/// </summary>
public class RankFusionTests
{
    /// <summary>
    /// Verifies that an item high in both lists beats items top in only one.
    /// </summary>
    [Fact]
    public void AnItemHighInBothLists_BeatsItemsTopInOnlyOne()
    {
        var fused = RankFusion.Fuse<string>(new IReadOnlyList<string>[]
        {
            new[] { "a", "b", "c" },
            new[] { "d", "b", "e" },
        });

        Assert.Equal("b", fused[0].Item);
        Assert.Equal(5, fused.Count);
    }

    /// <summary>
    /// Verifies that ties keep the first seen order.
    /// </summary>
    [Fact]
    public void Ties_KeepTheFirstSeenOrder()
    {
        var fused = RankFusion.Fuse<string>(new IReadOnlyList<string>[] { new[] { "a" }, new[] { "b" } });

        Assert.Equal(new[] { "a", "b" }, fused.Select(x => x.Item));
    }

    /// <summary>
    /// Verifies that empty lists give nothing.
    /// </summary>
    [Fact]
    public void EmptyLists_GiveNothing()
    {
        Assert.Empty(RankFusion.Fuse<string>(new IReadOnlyList<string>[] { new string[0], new string[0] }));
    }

    /// <summary>
    /// Verifies that the score is the sum of reciprocal ranks.
    /// </summary>
    [Fact]
    public void TheScore_IsTheSumOfReciprocalRanks()
    {
        var fused = RankFusion.Fuse<string>(new IReadOnlyList<string>[] { new[] { "a" }, new[] { "a" } });

        Assert.Equal(2.0 / (RankFusion.K + 1), fused[0].Score, 10);
    }
}
