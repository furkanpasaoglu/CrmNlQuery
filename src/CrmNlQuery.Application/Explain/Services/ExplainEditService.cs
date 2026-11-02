using System.Globalization;
using System.Xml.Linq;
using CrmNlQuery.Application.Ask.Planning.Validation;
using CrmNlQuery.Application.Observability.AccessLog;
using CrmNlQuery.Application.Ask.Models;
using CrmNlQuery.Application.Explain.Models;
using CrmNlQuery.Application.History.Abstractions;
using CrmNlQuery.Application.History.Services;
using CrmNlQuery.Application.Localization.Models;
using CrmNlQuery.Application.Localization.Services;
using CrmNlQuery.Domain.Access.Entities;

namespace CrmNlQuery.Application.Explain.Services;

/// <summary>
/// Removes or changes a filter chip of an explanation without calling the model, re-validating and running the stored query as the caller.
/// </summary>
public sealed class ExplainEditService
{
    public const int MaxTextLength = 200;

    private readonly IHistoryStore _history;
    private readonly QueryExplainer _explainer;
    private readonly PlanValidator _validator;
    private readonly ResultService _results;
    private readonly ChatService _chats;

    public ExplainEditService(IHistoryStore history, QueryExplainer explainer, PlanValidator validator, ResultService results, ChatService chats)
    {
        _history = history;
        _explainer = explainer;
        _validator = validator;
        _results = results;
        _chats = chats;
    }

    private static FailureOutcome NotAllowed() => new FailureOutcome(MessageCatalog.Now(Msg.EditNotAllowed), "edit_not_allowed");

    private static FailureOutcome Invalid() => new FailureOutcome(MessageCatalog.Now(Msg.EditInvalid), "edit_invalid");

    /// <summary>
    /// Applies an edit to a stored result's query, validates it, runs it as the caller and returns the new result.
    /// </summary>
    public async Task<AskOutcome?> EditAsync(string resultId, Guid caller, ExplainEdit edit, CancellationToken ct = default)
    {
        var entry = await _history.GetAsync(caller, resultId, ct);
        if (entry is null || entry.Kind is not null)
        {
            return null;
        }

        var explanation = await _explainer.ExplainAsync(entry.FetchXml, null, ct);
        if (explanation is null || explanation.Aggregate || IsFixedIdList(explanation))
        {
            return NotAllowed();
        }
        var chip = explanation.Filters.FirstOrDefault(f => f.Id == edit.ChipId);
        if (chip is null)
        {
            return new FailureOutcome(MessageCatalog.Now(Msg.EditUnknownFilter), "edit_unknown_filter");
        }
        if (chip.Group is not null)
        {
            return NotAllowed();
        }

        XDocument doc;
        try
        {
            doc = XDocument.Parse(entry.FetchXml);
        }
        catch (System.Xml.XmlException)
        {
            return NotAllowed();
        }
        var entity = doc.Root?.Element("entity");
        var conditions = entity is null ? new List<XElement>() : QueryExplainer.Conditions(entity).ToList();
        var index = int.TryParse(edit.ChipId.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number - 1 : -1;
        if (entity is null || index < 0 || index >= conditions.Count)
        {
            return new FailureOutcome(MessageCatalog.Now(Msg.EditUnknownFilter), "edit_unknown_filter");
        }
        var condition = conditions[index];

        string description;
        if (edit.Action == "remove")
        {
            var parent = condition.Parent;
            condition.Remove();
            RemoveEmptyAncestors(parent);
            description = "removed " + chip.FieldLabel;
        }
        else if (edit.Action == "set")
        {
            if (chip.Edit is null)
            {
                return NotAllowed();
            }
            var newValue = ValidValue(chip.Edit, edit.Value, condition);
            if (newValue is null)
            {
                return Invalid();
            }
            condition.Elements("value").Remove();
            try
            {
                condition.SetAttributeValue("value", newValue);
            }
            catch (ArgumentException)
            {
                return Invalid();
            }
            description = chip.FieldLabel + " changed";
        }
        else
        {
            return Invalid();
        }

        var validation = await _validator.ValidateFetchXmlAsync(doc.ToString(SaveOptions.DisableFormatting), ct);
        if (!validation.IsValid || validation.NormalizedFetchXml is null)
        {
            return Invalid();
        }

        using var accessScope = AccessLogContext.Begin("result");
        var answer = await _results.ExecuteStoredAsync(entry.Entity, validation.NormalizedFetchXml, Caller.User(caller), ct);
        if (answer is null)
        {
            return null;
        }
        var original = entry.Question;
        var marker = original.IndexOf(" (edited: ", StringComparison.Ordinal);
        var root = marker > 0 ? original.Substring(0, marker) : original;
        var question = Shorten(root) + " (edited: " + description + ")";
        var chat = await _chats.EnsureAsync(caller, entry.ConversationId, question, ct);
        return await _results.PublishAsync(answer, caller, question, resultId, chat, ct, adminViewable: false);
    }

    private static bool IsFixedIdList(QueryExplanation explanation)
    {
        return explanation.Filters.Any(f => f.Values.Count >= 2 && f.Values.All(v => v.Value is null && v.Label is null));
    }

    private static string? ValidValue(FilterEdit edit, string? value, XElement condition)
    {
        var text = (value ?? "").Trim();
        switch (edit.Kind)
        {
            case "option":
                return edit.Options is not null && edit.Options.Any(o => o.Value == text) ? text : null;
            case "integer":
                return long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var whole)
                    ? whole.ToString(CultureInfo.InvariantCulture) : null;
            case "number":
                return decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                    ? number.ToString(CultureInfo.InvariantCulture) : null;
            case "text":
                var clean = text.Replace("%", "");
                if (clean.Trim().Length == 0 || clean.Length > MaxTextLength || clean.Any(char.IsControl) || HasUnpairedSurrogate(clean))
                {
                    return null;
                }
                clean = clean.Trim();
                var op = ((string?)condition.Attribute("operator") ?? "").ToLowerInvariant();
                if (op is "like" or "not-like")
                {
                    if (clean.IndexOfAny(new[] { '_', '[', ']' }) >= 0)
                    {
                        return null;
                    }
                    var original = (string?)condition.Attribute("value") ?? "";
                    return (original.StartsWith('%') ? "%" : "") + clean + (original.EndsWith('%') ? "%" : "");
                }
                return clean;
        }
        return null;
    }

    private static void RemoveEmptyAncestors(XElement? parent)
    {
        while (parent is not null && parent.Name.LocalName == "filter" && !parent.HasElements)
        {
            var up = parent.Parent;
            parent.Remove();
            parent = up;
        }
    }

    private static bool HasUnpairedSurrogate(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                i++;
            }
            else if (char.IsSurrogate(text[i]))
            {
                return true;
            }
        }
        return false;
    }

    private static string Shorten(string question)
    {
        var clean = question.Trim();
        return clean.Length <= 200 ? clean : clean.Substring(0, 199) + "…";
    }
}
