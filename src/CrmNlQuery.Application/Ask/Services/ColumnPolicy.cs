using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.Ask.Models;
using CrmNlQuery.Application.Localization.Models;
using CrmNlQuery.Application.Localization.Services;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Domain.Metadata.Entities;
using CrmNlQuery.Domain.Metadata.Services;

namespace CrmNlQuery.Application.Ask.Services;

/// <summary>
/// Decides which columns to show: from the user's answer, the default view, or a column question.
/// </summary>
public sealed class ColumnPolicy
{
    private const int MaxDefaultColumns = 12;
    private readonly IDefaultViewSource _views;

    public ColumnPolicy(IDefaultViewSource views)
    {
        _views = views;
    }

    /// <summary>
    /// Decides the result columns from the analysis, the user's answer and the primary entity, or asks a column question.
    /// </summary>
    public async Task<ColumnDecision> DecideAsync(QuestionAnalysis analysis, string? answer, EntityInfo primary, CancellationToken ct = default)
    {
        IReadOnlyList<string> requested = analysis.Columns;

        if (answer == "default")
        {
            return Use(await DefaultColumnsAsync(primary, ct));
        }
        if (answer == "name")
        {
            return Use(new List<string> { primary.PrimaryNameAttribute });
        }
        if (answer is not null && answer.StartsWith("custom:", StringComparison.Ordinal))
        {
            requested = answer.Substring("custom:".Length)
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        }

        if (requested.Count == 0)
        {
            return new ColumnDecision(null, ColumnsQuestion(primary));
        }

        var resolved = new List<string>();
        for (var i = 0; i < requested.Count; i++)
        {
            var match = AttributeMatcher.Match(requested[i], primary);
            if (match.Attribute is not null)
            {
                resolved.Add(match.Attribute.LogicalName);
                continue;
            }
            return new ColumnDecision(null, UnknownColumnQuestion(requested, i, match, primary));
        }
        return Use(resolved);
    }

    private static ColumnDecision Use(IReadOnlyList<string> columns)
    {
        return new ColumnDecision(columns, null);
    }

    private async Task<IReadOnlyList<string>> DefaultColumnsAsync(EntityInfo primary, CancellationToken ct)
    {
        var fromView = await _views.GetDefaultColumnsAsync(primary.LogicalName, ct);
        var valid = fromView.Where(c => primary.FindAttribute(c) is not null).Distinct().Take(MaxDefaultColumns).ToList();
        if (valid.Count == 0)
        {
            valid = new List<string> { primary.PrimaryIdAttribute, primary.PrimaryNameAttribute };
        }
        return valid;
    }

    private static QuestionOutcome ColumnsQuestion(EntityInfo primary)
    {
        return new QuestionOutcome("columns", "columns",
            MessageCatalog.Now(Msg.ClarColumnsQuestion, MessageCatalog.Args(("label", primary.Label))),
            new List<Choice> { new Choice(MessageCatalog.Now(Msg.ClarDefaultColumns), "default"), new Choice(MessageCatalog.Now(Msg.ClarNameOnly), "name") },
            true);
    }

    private static QuestionOutcome UnknownColumnQuestion(IReadOnlyList<string> requested, int badIndex, MatchResult match, EntityInfo primary)
    {
        var choices = new List<Choice>();
        foreach (var suggestion in match.Suggestions)
        {
            var replaced = requested.ToList();
            replaced[badIndex] = suggestion.LogicalName;
            choices.Add(new Choice($"{suggestion.LogicalName} ({suggestion.Label})", "custom:" + string.Join(",", replaced)));
        }
        choices.Add(new Choice(MessageCatalog.Now(Msg.ClarDefaultColumns), "default"));
        return new QuestionOutcome("columns", "columns",
            MessageCatalog.Now(Msg.ClarUnknownColumn, MessageCatalog.Args(("column", requested[badIndex]), ("label", primary.Label))),
            choices, true);
    }
}
