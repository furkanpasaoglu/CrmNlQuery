using CrmNlQuery.Application.Ask.Models;
using CrmNlQuery.Application.Ask.Services;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Eval.Models;

namespace CrmNlQuery.Eval.Services;

/// <summary>
/// Runs eval cases against the ask pipeline and compares outcomes.
/// </summary>
public static class EvalRunner
{
    private static string KindOf(AskOutcome outcome)
    {
        return outcome switch
        {
            AnswerOutcome => "answer",
            SummaryOutcome => "summary",
            DraftOutcome => "draft",
            QuestionOutcome => "question",
            _ => "failure",
        };
    }

    /// <summary>
    /// Returns PASS, FAIL or FLAKY for repeated runs depending on how many passed.
    /// </summary>
    public static string Verdict(int passes, int runs)
    {
        if (runs < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(runs), "At least one run is needed.");
        }
        if (passes == runs) { return "PASS"; }
        return passes == 0 ? "FAIL" : "FLAKY";
    }

    /// <summary>
    /// Collects all table and field names used in a FetchXML query.
    /// </summary>
    public static HashSet<string> UsedNames(string fetchXml)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var element in System.Xml.Linq.XDocument.Parse(fetchXml).Descendants())
            {
                foreach (var key in new[] { "name", "attribute", "from", "to" })
                {
                    var value = (string?)element.Attribute(key);
                    if (!string.IsNullOrEmpty(value)) { names.Add(value); }
                }
            }
        }
        catch (System.Xml.XmlException) { }
        return names;
    }

    /// <summary>
    /// Compares an eval case expectation with the outcome, purely without network or model.
    /// </summary>
    public static (bool Pass, string Detail) Evaluate(EvalCase c, AskOutcome final, AskOutcome first)
    {
        var e = c.Expect;
        if (e.FinalKind == "not-draft")
        {
            return KindOf(final) != "draft" ? (true, "ok") : (false, "was treated as an email draft, expected a normal answer");
        }
        if (final is DraftOutcome draft && e.FinalKind == "draft")
        {
            var template = (draft.TemplateSubject + " " + draft.TemplateBody).ToLowerInvariant();
            if (e.Entity is not null && !string.Equals(draft.Entity, e.Entity, StringComparison.OrdinalIgnoreCase))
            {
                return (false, $"draft entity was '{draft.Entity}', expected '{e.Entity}'");
            }
            foreach (var needed in e.MustContain ?? new List<string>())
            {
                if (!template.Contains(needed.ToLowerInvariant())) { return (false, $"the draft template does not contain '{needed}'"); }
            }
            foreach (var forbidden in e.MustNotContain ?? new List<string>())
            {
                if (template.Contains(forbidden.ToLowerInvariant())) { return (false, $"the draft template contains '{forbidden}'"); }
            }
            if (e.MustContainAny is { Count: > 0 } any && !any.Any(a => template.Contains(a.ToLowerInvariant())))
            {
                return (false, $"the draft template contains none of [{string.Join(", ", any)}]");
            }
            return (true, "ok: " + draft.TemplateSubject);
        }
        if (e.FinalKind == "not-summary")
        {
            return KindOf(final) != "summary" ? (true, "ok") : (false, "was treated as a customer summary, expected a normal answer");
        }
        if (final is SummaryOutcome summary && e.FinalKind == "summary")
        {
            return e.Entity is not null && !string.Equals(summary.Entity, e.Entity, StringComparison.OrdinalIgnoreCase)
                ? (false, $"summary entity was '{summary.Entity}', expected '{e.Entity}'")
                : (true, "ok");
        }
        if (e.FirstKind is not null && KindOf(first) != e.FirstKind)
        {
            return (false, $"first outcome was '{KindOf(first)}', expected '{e.FirstKind}'");
        }
        if (e.FirstKeyPrefix is not null)
        {
            var key = (first as QuestionOutcome)?.AnswerKey ?? "";
            if (!key.StartsWith(e.FirstKeyPrefix, StringComparison.Ordinal))
            {
                return (false, $"first question key '{key}' does not start with '{e.FirstKeyPrefix}'");
            }
        }
        if (e.FinalKind != "any" && KindOf(final) != e.FinalKind)
        {
            var extra = final is FailureOutcome f ? " (" + f.Message + ")" : final is QuestionOutcome q ? " (" + q.Text + ")" : "";
            return (false, $"final outcome was '{KindOf(final)}', expected '{e.FinalKind}'{extra}");
        }
        if (final is not AnswerOutcome answer)
        {
            return (true, "ok");
        }
        if (e.FollowUp is not null && (answer.BasedOn is not null) != e.FollowUp)
        {
            return (false, e.FollowUp == true ? "was treated as a new question, expected a follow-up" : $"was treated as a follow-up of \"{answer.BasedOn}\", expected a new question");
        }

        if (e.UsesContext is not null && (answer.AboutRecord is not null) != e.UsesContext)
        {
            return (false, e.UsesContext == true ? "did not use the open record, expected it to be used" : $"used the open record '{answer.AboutRecord}', expected it to be ignored");
        }

        if (e.MustNotUse is { Count: > 0 })
        {
            var used = UsedNames(answer.FetchXml);
            foreach (var forbidden in e.MustNotUse)
            {
                if (used.Contains(forbidden))
                {
                    return (false, $"the query uses closed data '{forbidden}'");
                }
            }
        }
        if (e.MustUseFetch is { Count: > 0 })
        {
            var fetch = answer.FetchXml ?? "";
            foreach (var needed in e.MustUseFetch)
            {
                if (!fetch.Contains(needed, StringComparison.OrdinalIgnoreCase))
                {
                    return (false, $"the query does not contain '{needed}'");
                }
            }
        }
        if (e.MustNotUseFetch is { Count: > 0 })
        {
            var fetch = answer.FetchXml ?? "";
            foreach (var forbidden in e.MustNotUseFetch)
            {
                if (fetch.Contains(forbidden, StringComparison.OrdinalIgnoreCase))
                {
                    return (false, $"the query contains '{forbidden}' but should not");
                }
            }
        }
        if (e.Entity is not null && !string.Equals(answer.Entity, e.Entity, StringComparison.OrdinalIgnoreCase))
        {
            return (false, $"entity was '{answer.Entity}', expected '{e.Entity}'");
        }
        if (e.Columns is not null && !answer.Table.Columns.SequenceEqual(e.Columns, StringComparer.OrdinalIgnoreCase))
        {
            return (false, $"columns were [{string.Join(", ", answer.Table.Columns)}], expected [{string.Join(", ", e.Columns)}]");
        }
        if (e.MinRows is not null && answer.Table.Rows.Count < e.MinRows)
        {
            return (false, $"{answer.Table.Rows.Count} rows, expected at least {e.MinRows}");
        }
        if (e.RowCount is not null && answer.Table.Rows.Count != e.RowCount)
        {
            return (false, $"{answer.Table.Rows.Count} rows, expected exactly {e.RowCount}");
        }

        var cells = answer.Table.Rows.SelectMany(r => r).ToList();
        foreach (var text in e.MustContain ?? new List<string>())
        {
            if (!cells.Any(cell => cell.Contains(text, StringComparison.OrdinalIgnoreCase)))
            {
                return (false, $"results do not contain '{text}'");
            }
        }
        foreach (var text in e.MustNotContain ?? new List<string>())
        {
            if (cells.Any(cell => cell.Contains(text, StringComparison.OrdinalIgnoreCase)))
            {
                return (false, $"results must not contain '{text}'");
            }
        }
        return (true, "ok");
    }

    /// <summary>
    /// Resolves the case context record on the pipeline for an eval case.
    /// </summary>
    public static async Task<ResolvedRecord?> ResolveContextAsync(EvalCase c, Func<string, string, CancellationToken, Task<ResolvedRecord?>> finder, CancellationToken ct)
    {
        if (c.Context is null) { return null; }
        return await finder(c.Context.Entity, c.Context.Name, ct)
            ?? throw new InvalidOperationException($"Context record not found: {c.Context.Entity} '{c.Context.Name}' (case {c.Id})");
    }

    /// <summary>
    /// Runs one eval case on the pipeline, answering clarifying questions with the case answers, and returns first and final outcomes.
    /// </summary>
    public static async Task<(AskOutcome First, AskOutcome Final)> RunCaseAsync(AskPipeline pipeline, EvalCase c, CancellationToken ct, PreviousQuery? previous = null, ResolvedRecord? current = null)
    {
        var answers = new Dictionary<string, string>();
        var first = await pipeline.RunAsync(new AskRequest(c.Question, answers, CrmNlQuery.Domain.Access.Entities.Caller.ForEvaluation(), previous, CurrentRecord: current), ct);
        var last = first;

        for (var round = 0; round < 5 && last is QuestionOutcome q; round++)
        {
            string? value = null;
            if (c.Answers is not null && c.Answers.TryGetValue(q.AnswerKey, out var fixedAnswer))
            {
                value = fixedAnswer;
            }
            else if (c.PickChoice is int pick && q.Choices.Count > pick)
            {
                value = q.Choices[pick].Value;
            }
            if (value is null || q.AnswerKey == "")
            {
                break;
            }
            answers[q.AnswerKey] = value;
            last = await pipeline.RunAsync(new AskRequest(c.Question, answers, CrmNlQuery.Domain.Access.Entities.Caller.ForEvaluation(), previous, CurrentRecord: current), ct);
        }
        return (first, last);
    }
}
