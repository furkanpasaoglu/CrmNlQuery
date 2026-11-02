using CrmNlQuery.Tests.Support;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.Ask.Services;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Domain.Metadata.Entities;

namespace CrmNlQuery.Tests.Application.Ask.Services;

/// <summary>
/// Tests how columns are chosen: asking, explicit columns, default view and name-only answers.
/// </summary>
public class ColumnPolicyTests
{
    private sealed class FakeViews : IDefaultViewSource
    {
        private readonly IReadOnlyList<string> _columns;
        public FakeViews(params string[] columns) { _columns = columns; }
        /// <summary>
        /// Fake view source returning default columns for the column policy tests.
        /// </summary>
        public Task<IReadOnlyList<string>> GetDefaultColumnsAsync(string entityLogicalName, CancellationToken ct = default)
            => Task.FromResult(_columns);
    }

    private static QuestionAnalysis Analysis(params string[] columns) => new QuestionAnalysis(
        "product", new List<string>(), new List<Mention>(), columns, false);

    private static readonly EntityInfo Product = TestMetadata.Product();

    /// <summary>
    /// Verifies that no columns and no answer asks the column question.
    /// </summary>
    [Fact]
    public async Task NoColumnsAndNoAnswer_AsksColumnQuestion()
    {
        var decision = await new ColumnPolicy(new FakeViews()).DecideAsync(Analysis(), null, Product);
        Assert.Null(decision.Columns);
        Assert.Equal("columns", decision.Question!.AnswerKey);
        Assert.Contains(decision.Question.Choices, c => c.Value == "default");
        Assert.Contains(decision.Question.Choices, c => c.Value == "name");
        Assert.True(decision.Question.AllowFreeText);
    }

    /// <summary>
    /// Verifies that explicit columns are resolved without a question.
    /// </summary>
    [Fact]
    public async Task ExplicitColumns_AreResolved_WithoutQuestion()
    {
        var decision = await new ColumnPolicy(new FakeViews()).DecideAsync(Analysis("id", "name"), null, Product);
        Assert.Null(decision.Question);
        Assert.Equal(new[] { "productid", "name" }, decision.Columns);
    }

    /// <summary>
    /// Verifies that the default answer uses the default view columns and drops unknown ones.
    /// </summary>
    [Fact]
    public async Task DefaultAnswer_UsesDefaultViewColumns_DroppingUnknownOnes()
    {
        var views = new FakeViews("name", "productnumber", "bogus");
        var decision = await new ColumnPolicy(views).DecideAsync(Analysis(), "default", Product);
        Assert.Equal(new[] { "name", "productnumber" }, decision.Columns);
    }

    /// <summary>
    /// Verifies that the default answer without a default view falls back to id and name.
    /// </summary>
    [Fact]
    public async Task DefaultAnswer_WithoutDefaultView_FallsBackToIdAndName()
    {
        var decision = await new ColumnPolicy(new FakeViews()).DecideAsync(Analysis(), "default", Product);
        Assert.Equal(new[] { "productid", "name" }, decision.Columns);
    }

    /// <summary>
    /// Verifies that a name answer returns only the primary name.
    /// </summary>
    [Fact]
    public async Task NameAnswer_ReturnsOnlyPrimaryName()
    {
        var decision = await new ColumnPolicy(new FakeViews()).DecideAsync(Analysis(), "name", Product);
        Assert.Equal(new[] { "name" }, decision.Columns);
    }

    /// <summary>
    /// Verifies that a custom answer is resolved.
    /// </summary>
    [Fact]
    public async Task CustomAnswer_IsResolved()
    {
        var decision = await new ColumnPolicy(new FakeViews()).DecideAsync(Analysis(), "custom:id, Product Number", Product);
        Assert.Equal(new[] { "productid", "productnumber" }, decision.Columns);
    }

    /// <summary>
    /// Verifies that an unknown column asks with a suggestion.
    /// </summary>
    [Fact]
    public async Task UnknownColumn_AsksWithSuggestion()
    {
        var decision = await new ColumnPolicy(new FakeViews()).DecideAsync(Analysis("productname"), null, Product);
        Assert.Null(decision.Columns);
        Assert.Contains("productname", decision.Question!.Text);
        Assert.Contains(decision.Question.Choices, c => c.Value == "custom:name");
    }

    /// <summary>
    /// Verifies that default view columns are capped at twelve.
    /// </summary>
    [Fact]
    public async Task DefaultViewColumns_AreCappedAtTwelve()
    {
        var attributes = Enumerable.Range(1, 15)
            .Select(i => new AttributeInfo("a" + i, "A" + i, "String", new List<OptionInfo>())).ToList();
        var wide = new EntityInfo("wide", "wides", "Wide", "a1", "a2", attributes, new List<RelationshipInfo>());
        var views = new FakeViews(attributes.Select(a => a.LogicalName).ToArray());

        var decision = await new ColumnPolicy(views).DecideAsync(Analysis(), "default", wide);

        Assert.Equal(12, decision.Columns!.Count);
    }
}
