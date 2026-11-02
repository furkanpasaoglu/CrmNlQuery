using CrmNlQuery.Application.Quality.Comparison;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Eval.Services;

namespace CrmNlQuery.Tests.Application.Quality.Comparison;

/// <summary>
/// Tests the reference-query comparer: row order, multisets and sets, column matching, null versus zero, tolerance and boundary ties.
/// </summary>
public class ReferenceComparerTests
{
    private static ResultTable Table(string[] columns, params string[][] rows)
        => new ResultTable(columns, rows.Select(r => (IReadOnlyList<string>)r).ToList());

    private static ResultTable Aggregate(string[] columns, params string[][] raw)
        => new ResultTable(columns, raw.Select(r => (IReadOnlyList<string>)r).ToList()) { Raw = raw.Select(r => (IReadOnlyList<string>)r).ToList() };

    /// <summary>
    /// Verifies that same rows in another order pass when the reference has no order.
    /// </summary>
    [Fact]
    public void SameRowsInAnotherOrder_Pass_WhenTheReferenceHasNoOrder()
    {
        var reference = Table(new[] { "name" }, new[] { "A" }, new[] { "B" }, new[] { "C" });
        var candidate = Table(new[] { "name" }, new[] { "C" }, new[] { "A" }, new[] { "B" });

        Assert.True(ReferenceComparer.Compare(candidate, reference, null, referenceHasOrder: false).Pass);
    }

    /// <summary>
    /// Verifies that a different order fails when the reference is ordered.
    /// </summary>
    [Fact]
    public void ADifferentOrder_Fails_WhenTheReferenceIsOrdered()
    {
        var reference = Table(new[] { "name" }, new[] { "A" }, new[] { "B" });
        var candidate = Table(new[] { "name" }, new[] { "B" }, new[] { "A" });

        var result = ReferenceComparer.Compare(candidate, reference, null, referenceHasOrder: true);

        Assert.False(result.Pass);
        Assert.Contains("order matters", result.Detail);
    }

    /// <summary>
    /// Verifies that explicit order rule overrides the reference order.
    /// </summary>
    [Fact]
    public void ExplicitOrderRule_OverridesTheReferenceOrder()
    {
        var reference = Table(new[] { "name" }, new[] { "A" }, new[] { "B" });
        var candidate = Table(new[] { "name" }, new[] { "B" }, new[] { "A" });

        Assert.True(ReferenceComparer.Compare(candidate, reference, new CompareRule { Order = "unordered" }, referenceHasOrder: true).Pass);
    }

    /// <summary>
    /// Verifies that extra and missing rows are named in the detail.
    /// </summary>
    [Fact]
    public void ExtraAndMissingRows_AreNamedInTheDetail()
    {
        var reference = Table(new[] { "name" }, new[] { "A" }, new[] { "B" });
        var candidate = Table(new[] { "name" }, new[] { "A" }, new[] { "X" }, new[] { "Y" });

        var result = ReferenceComparer.Compare(candidate, reference, null, false);

        Assert.False(result.Pass);
        Assert.Contains("missing 1: b", result.Detail);
        Assert.Contains("extra 2", result.Detail);
    }

    /// <summary>
    /// Verifies that duplicate rows matter, for a multiset, and but not for a set.
    /// </summary>
    [Fact]
    public void DuplicateRows_Matter_ForAMultiset_ButNotForASet()
    {
        var reference = Table(new[] { "name" }, new[] { "A" }, new[] { "B" });
        var candidate = Table(new[] { "name" }, new[] { "A" }, new[] { "A" }, new[] { "B" });

        Assert.False(ReferenceComparer.Compare(candidate, reference, null, false).Pass);
        Assert.True(ReferenceComparer.Compare(candidate, reference, new CompareRule { Duplicates = "set" }, false).Pass);
    }

    /// <summary>
    /// Verifies that extra candidate columns are allowed by default but not when exact.
    /// </summary>
    [Fact]
    public void ExtraCandidateColumns_AreAllowedByDefault_ButNotWhenExact()
    {
        var reference = Table(new[] { "name" }, new[] { "A" }, new[] { "B" });
        var candidate = Table(new[] { "name", "city" }, new[] { "A", "X" }, new[] { "B", "Y" });

        Assert.True(ReferenceComparer.Compare(candidate, reference, null, false).Pass);
        var strict = ReferenceComparer.Compare(candidate, reference, new CompareRule { Columns = "exact" }, false);
        Assert.False(strict.Pass);
        Assert.Contains("exactly", strict.Detail);
    }

    /// <summary>
    /// Verifies that a missing reference column fails with its name.
    /// </summary>
    [Fact]
    public void AMissingReferenceColumn_Fails_WithItsName()
    {
        var reference = Table(new[] { "name", "revenue" }, new[] { "A", "1" });
        var candidate = Table(new[] { "name" }, new[] { "A" });

        var result = ReferenceComparer.Compare(candidate, reference, null, false);

        Assert.False(result.Pass);
        Assert.Contains("revenue", result.Detail);
    }

    /// <summary>
    /// Verifies that aggregate aliases are matched by position when the column count is equal.
    /// </summary>
    [Fact]
    public void AggregateAliases_AreMatchedByPosition_WhenTheColumnCountIsEqual()
    {
        var reference = Aggregate(new[] { "city", "total" }, new[] { "Ankara", "2" }, new[] { "Izmir", "1" });
        var candidate = Aggregate(new[] { "city", "n" }, new[] { "Izmir", "1" }, new[] { "Ankara", "2" });

        Assert.True(ReferenceComparer.Compare(candidate, reference, null, false).Pass);
    }

    /// <summary>
    /// Verifies that last column rule compares only the measure whatever the group key looks like.
    /// </summary>
    [Fact]
    public void LastColumnRule_ComparesOnlyTheMeasure_WhateverTheGroupKeyLooksLike()
    {
        var reference = Aggregate(new[] { "account", "n" }, new[] { "guid-1", "1" }, new[] { "guid-2", "3" });
        var candidate = Aggregate(new[] { "name", "total" }, new[] { "Acme", "3" }, new[] { "Beta", "1" });
        var rule = new CompareRule { Columns = "last" };

        Assert.True(ReferenceComparer.Compare(candidate, reference, rule, false).Pass);
        var wrong = Aggregate(new[] { "name", "total" }, new[] { "Acme", "2" }, new[] { "Beta", "1" });
        Assert.False(ReferenceComparer.Compare(wrong, reference, rule, false).Pass);
        var fewer = Aggregate(new[] { "name", "total" }, new[] { "Acme", "3" });
        Assert.False(ReferenceComparer.Compare(fewer, reference, rule, false).Pass);
    }

    /// <summary>
    /// Verifies that a null is never equal to zero.
    /// </summary>
    [Fact]
    public void ANullIsNeverEqualToZero()
    {
        var reference = Aggregate(new[] { "city", "total" }, new[] { "Ankara", "0" });
        var candidate = Aggregate(new[] { "city", "total" }, new[] { "Ankara", "" });

        Assert.False(ReferenceComparer.Compare(candidate, reference, null, false).Pass);
    }

    /// <summary>
    /// Verifies that numbers within the tolerance match.
    /// </summary>
    [Fact]
    public void NumbersWithinTheTolerance_Match()
    {
        var reference = Aggregate(new[] { "city", "total" }, new[] { "Ankara", "100.004" });
        var candidate = Aggregate(new[] { "city", "total" }, new[] { "Ankara", "100.00" });

        Assert.True(ReferenceComparer.Compare(candidate, reference, new CompareRule { Tolerance = 0.01 }, false).Pass);
        Assert.False(ReferenceComparer.Compare(candidate, Aggregate(new[] { "city", "total" }, new[] { "Ankara", "101" }), new CompareRule { Tolerance = 0.01 }, false).Pass);
    }

    /// <summary>
    /// Verifies that boundary ties accept any reference member.
    /// </summary>
    [Fact]
    public void BoundaryTies_AcceptAnyReferenceMember()
    {
        var reference = Table(new[] { "name" }, new[] { "A" }, new[] { "B" }, new[] { "C" }, new[] { "D" }, new[] { "E" });
        var candidate = Table(new[] { "name" }, new[] { "A" }, new[] { "C" }, new[] { "E" });

        Assert.True(ReferenceComparer.Compare(candidate, reference, new CompareRule { Ties = "boundary" }, true).Pass);
        Assert.False(ReferenceComparer.Compare(candidate, reference, null, true).Pass);
        var outsider = Table(new[] { "name" }, new[] { "A" }, new[] { "Z" });
        Assert.False(ReferenceComparer.Compare(outsider, reference, new CompareRule { Ties = "boundary" }, true).Pass);
    }

    /// <summary>
    /// Verifies that two empty results match.
    /// </summary>
    [Fact]
    public void TwoEmptyResults_Match()
    {
        var empty = Table(new[] { "name" });

        Assert.True(ReferenceComparer.Compare(empty, empty, null, false).Pass);
    }

    /// <summary>
    /// Verifies that reference helpers add the row cap and detect order.
    /// </summary>
    [Fact]
    public void ReferenceHelpers_AddTheRowCap_AndDetectOrder()
    {
        Assert.Contains("top=\"500\"", ReferenceQuery.WithRowCap("<fetch><entity name=\"account\"><attribute name=\"name\" /></entity></fetch>"));
        Assert.DoesNotContain("top=", ReferenceQuery.WithRowCap("<fetch aggregate=\"true\"><entity name=\"account\"><attribute name=\"name\" groupby=\"true\" alias=\"n\" /></entity></fetch>"));
        Assert.Contains("top=\"5\"", ReferenceQuery.WithRowCap("<fetch top=\"5\"><entity name=\"account\" /></fetch>"));
        Assert.True(ReferenceQuery.HasOrder("<fetch><entity name=\"account\"><order attribute=\"name\" /></entity></fetch>"));
        Assert.False(ReferenceQuery.HasOrder("<fetch><entity name=\"account\" /></fetch>"));
    }

    /// <summary>
    /// Verifies that the fingerprint changes when the data changes but not with the row order.
    /// </summary>
    [Fact]
    public void TheFingerprint_ChangesWhenTheDataChanges_ButNotWithTheRowOrder()
    {
        var a = Table(new[] { "name" }, new[] { "A" }, new[] { "B" });
        var sameDifferentOrder = Table(new[] { "name" }, new[] { "B" }, new[] { "A" });
        var changed = Table(new[] { "name" }, new[] { "A" }, new[] { "B" }, new[] { "C" });

        Assert.Equal(ReferenceCheck.Fingerprint(a), ReferenceCheck.Fingerprint(sameDifferentOrder));
        Assert.NotEqual(ReferenceCheck.Fingerprint(a), ReferenceCheck.Fingerprint(changed));
        Assert.Equal(3, DataFingerprints.RowsOf(ReferenceCheck.Fingerprint(changed)));
    }
}
