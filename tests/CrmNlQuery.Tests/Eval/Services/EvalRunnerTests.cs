using CrmNlQuery.Application.Ask.Models;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Eval.Models;
using CrmNlQuery.Eval.Services;

namespace CrmNlQuery.Tests.Eval.Services;

/// <summary>
/// Tests the eval runner: case execution, expectations and result summary.
/// </summary>
public class EvalRunnerTests
{
    private static AnswerOutcome Answer(string entity, string[] columns, string[][] rows) =>
        new AnswerOutcome(entity, "u", "<fetch/>",
            new ResultTable(columns, rows.Select(r => (IReadOnlyList<string>)r.ToList()).ToList()), false);

    private static QuestionOutcome ColumnsQuestion() =>
        new QuestionOutcome("columns", "columns", "?", new List<Choice>(), true);

    /// <summary>
    /// Verifies that verdict is pass fail or flaky.
    /// </summary>
    [Theory]
    [InlineData(3, 3, "PASS")]
    [InlineData(0, 3, "FAIL")]
    [InlineData(1, 3, "FLAKY")]
    [InlineData(2, 3, "FLAKY")]
    [InlineData(1, 1, "PASS")]
    [InlineData(0, 1, "FAIL")]
    public void Verdict_IsPass_Fail_OrFlaky(int passes, int runs, string expected)
    {
        Assert.Equal(expected, EvalRunner.Verdict(passes, runs));
    }

    /// <summary>
    /// Verifies that must not use fails when the query touches closed data and any kind is accepted.
    /// </summary>
    [Fact]
    public void MustNotUse_FailsWhenTheQueryTouchesClosedData_AndAnyKindIsAccepted()
    {
        var withPhone = new AnswerOutcome("account", "u", "<fetch><entity name=\"account\"><attribute name=\"Telephone1\"/></entity></fetch>",
            new ResultTable(new[] { "x" }, new List<IReadOnlyList<string>>()), false);
        var expect = new EvalExpect { FinalKind = "any", MustNotUse = new List<string> { "telephone1" } };
        var (pass, detail) = EvalRunner.Evaluate(new EvalCase { Id = "x", Expect = expect }, withPhone, withPhone);
        Assert.False(pass);
        Assert.Contains("telephone1", detail);

        var failure = new FailureOutcome("no", "x");
        Assert.True(EvalRunner.Evaluate(new EvalCase { Id = "y", Expect = expect }, failure, failure).Pass);
    }

    /// <summary>
    /// Verifies that summary expectations are checked and not summary catches false triggers.
    /// </summary>
    [Fact]
    public void SummaryExpectations_AreChecked_AndNotSummaryCatchesFalseTriggers()
    {
        var summary = new SummaryOutcome("account", "Account", "id", "Brightwave", new List<CrmNlQuery.Application.Summary.Models.SummarySection>());
        var answer = Answer("product", new[] { "name" }, new[] { new[] { "Chair" } });

        Assert.True(EvalRunner.Evaluate(new EvalCase { Id = "a", Expect = new EvalExpect { FinalKind = "summary", Entity = "account" } }, summary, summary).Pass);
        Assert.False(EvalRunner.Evaluate(new EvalCase { Id = "b", Expect = new EvalExpect { FinalKind = "summary", Entity = "contact" } }, summary, summary).Pass);
        Assert.False(EvalRunner.Evaluate(new EvalCase { Id = "c", Expect = new EvalExpect { FinalKind = "summary", Entity = "account" } }, answer, answer).Pass);
        Assert.True(EvalRunner.Evaluate(new EvalCase { Id = "d", Expect = new EvalExpect { FinalKind = "not-summary" } }, answer, answer).Pass);
        Assert.False(EvalRunner.Evaluate(new EvalCase { Id = "e", Expect = new EvalExpect { FinalKind = "not-summary" } }, summary, summary).Pass);
    }

    /// <summary>
    /// Verifies that verdict with no runs throws.
    /// </summary>
    [Fact]
    public void Verdict_WithNoRuns_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => EvalRunner.Verdict(0, 0));
    }

    /// <summary>
    /// Verifies that passes when all expectations match.
    /// </summary>
    [Fact]
    public void Passes_WhenAllExpectationsMatch()
    {
        var expect = new EvalExpect { Entity = "product", Columns = new List<string> { "name" }, MustContain = new List<string> { "Chair" }, MinRows = 1 };
        var answer = Answer("product", new[] { "name" }, new[] { new[] { "Chair" } });
        var (pass, detail) = EvalRunner.Evaluate(new EvalCase { Id = "x", Expect = expect }, answer, answer);
        Assert.True(pass, detail);
    }

    /// <summary>
    /// Verifies that fails on wrong entity.
    /// </summary>
    [Fact]
    public void Fails_OnWrongEntity()
    {
        var expect = new EvalExpect { Entity = "contract" };
        var answer = Answer("product", new[] { "name" }, new[] { new[] { "Chair" } });
        Assert.False(EvalRunner.Evaluate(new EvalCase { Id = "x", Expect = expect }, answer, answer).Pass);
    }

    /// <summary>
    /// Verifies that fails when forbidden text appears.
    /// </summary>
    [Fact]
    public void Fails_WhenForbiddenTextAppears()
    {
        var expect = new EvalExpect { MustNotContain = new List<string> { "Denema" } };
        var answer = Answer("contract", new[] { "title" }, new[] { new[] { "Denema Annual Contract" } });
        Assert.False(EvalRunner.Evaluate(new EvalCase { Id = "x", Expect = expect }, answer, answer).Pass);
    }

    /// <summary>
    /// Verifies that fails when first kind differs.
    /// </summary>
    [Fact]
    public void Fails_WhenFirstKindDiffers()
    {
        var expect = new EvalExpect { FirstKind = "question" };
        var answer = Answer("product", new[] { "name" }, new[] { new[] { "Chair" } });
        Assert.False(EvalRunner.Evaluate(new EvalCase { Id = "x", Expect = expect }, answer, answer).Pass);
    }

    /// <summary>
    /// Verifies that first key prefix is checked.
    /// </summary>
    [Fact]
    public void FirstKeyPrefix_IsChecked()
    {
        var expect = new EvalExpect { FirstKind = "question", FirstKeyPrefix = "resolve:", FinalKind = "question" };
        var q = ColumnsQuestion();
        Assert.False(EvalRunner.Evaluate(new EvalCase { Id = "x", Expect = expect }, q, q).Pass);
    }

    /// <summary>
    /// Verifies that failure expected passes on failure.
    /// </summary>
    [Fact]
    public void FailureExpected_PassesOnFailure()
    {
        var expect = new EvalExpect { FinalKind = "failure" };
        var failure = new FailureOutcome("not found");
        Assert.True(EvalRunner.Evaluate(new EvalCase { Id = "x", Expect = expect }, failure, failure).Pass);
    }

    /// <summary>
    /// Verifies that row count is checked.
    /// </summary>
    [Fact]
    public void RowCount_IsChecked()
    {
        var expect = new EvalExpect { RowCount = 1 };
        var answer = Answer("account", new[] { "count" }, new[] { new[] { "19" }, new[] { "20" } });
        Assert.False(EvalRunner.Evaluate(new EvalCase { Id = "x", Expect = expect }, answer, answer).Pass);
    }
}
