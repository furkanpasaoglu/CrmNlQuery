using System.Xml;
using System.Xml.Linq;
using CrmNlQuery.Domain.Ask.Steps;
using CrmNlQuery.Application.Ask.Abstractions;
using CrmNlQuery.Application.Operations.Abstractions;
using CrmNlQuery.Application.Operations.Models;
using CrmNlQuery.Domain.Ask.Entities;
using CrmNlQuery.Domain.Metadata.Entities;

namespace CrmNlQuery.Application.Ask.Planning.Validation;

/// <summary>
/// Validates query plans and FetchXML against the schema, data policy and cost limits before execution.
/// </summary>
public sealed class PlanValidator
{
    public const int MaxRows = 500;

    private static readonly HashSet<string> AllowedElements = new HashSet<string>(StringComparer.Ordinal)
    {
        "fetch", "entity", "attribute", "filter", "condition", "value", "order", "link-entity"
    };

    private static readonly HashSet<string> AllowedFetchAttributes = new HashSet<string>(StringComparer.Ordinal)
    {
        "top", "distinct", "aggregate", "mapping", "version"
    };

    private static readonly HashSet<string> AllowedOperators = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "eq", "ne", "gt", "ge", "lt", "le", "like", "not-like", "begins-with", "not-begin-with", "ends-with",
        "not-end-with", "in", "not-in", "between", "not-between", "null", "not-null", "on", "on-or-before",
        "on-or-after", "today", "yesterday", "tomorrow", "this-week", "last-week", "this-month", "last-month",
        "next-month", "this-year", "last-year", "last-x-days", "next-x-days", "last-x-months", "last-x-years",
        "eq-userid", "contain-values", "not-contain-values"
    };

    private static readonly HashSet<string> NoValueOperators = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "null", "not-null", "today", "yesterday", "tomorrow", "this-week", "last-week", "this-month",
        "last-month", "next-month", "this-year", "last-year", "eq-userid"
    };

    private static readonly Dictionary<string, string[]> AllowedChildren = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["fetch"] = new[] { "entity" },
        ["entity"] = new[] { "attribute", "order", "filter", "link-entity" },
        ["link-entity"] = new[] { "attribute", "order", "filter", "link-entity" },
        ["filter"] = new[] { "filter", "condition" },
        ["condition"] = new[] { "value" },
        ["attribute"] = Array.Empty<string>(),
        ["order"] = Array.Empty<string>(),
        ["value"] = Array.Empty<string>()
    };

    private readonly IMetadataSource _metadata;

    private readonly ILimitsSource _limits;

    public PlanValidator(IMetadataSource metadata, ILimitsSource? limits = null)
    {
        _metadata = metadata;
        _limits = limits ?? FixedLimits.Default;
    }

    /// <summary>
    /// Validates a query plan against the schema and rules.
    /// </summary>
    public Task<ValidationResult> ValidateAsync(QueryPlan plan, CancellationToken ct = default)
    {
        return ValidateAsync(plan, null, ct);
    }

    /// <summary>
    /// Validates a query plan, allowing step placeholders for a dependent step.
    /// </summary>
    public async Task<ValidationResult> ValidateAsync(QueryPlan plan, StepContext? stepContext, CancellationToken ct = default)
    {
        if (plan.Steps.Count != 1)
        {
            return Fail("Plan must contain exactly one step.");
        }
        if (plan.Steps[0].RunOnlyIf is not null)
        {
            return Fail("runOnlyIf is not supported yet; leave it out.");
        }
        return await ValidateFetchXmlAsync(plan.Steps[0].FetchXml, stepContext, ct);
    }

    /// <summary>
    /// Validates a FetchXML string against the schema and rules.
    /// </summary>
    public Task<ValidationResult> ValidateFetchXmlAsync(string fetchXml, CancellationToken ct = default)
    {
        return ValidateFetchXmlAsync(fetchXml, null, ct);
    }

    /// <summary>
    /// Validates FetchXML, allowing step placeholders for a dependent multi-step plan step.
    /// </summary>
    public async Task<ValidationResult> ValidateFetchXmlAsync(string fetchXml, StepContext? stepContext, CancellationToken ct = default)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Parse(fetchXml);
        }
        catch (XmlException ex)
        {
            return Fail("Invalid XML: " + ex.Message);
        }

        var root = doc.Root!;
        if (root.Name.LocalName != "fetch")
        {
            return Fail("Root element must be <fetch>.");
        }

        var errors = new List<string>();
        foreach (var el in doc.Descendants())
        {
            var name = el.Name.LocalName;
            if (name == "all-attributes")
            {
                errors.Add("<all-attributes> is not allowed; list the attributes explicitly.");
            }
            else if (!AllowedElements.Contains(name))
            {
                errors.Add($"Element <{name}> is not allowed.");
            }
            else if (el.Parent is not null && AllowedChildren.TryGetValue(el.Parent.Name.LocalName, out var allowed)
                && !allowed.Contains(name))
            {
                errors.Add($"Element <{name}> is not allowed inside <{el.Parent.Name.LocalName}>.");
            }
        }
        foreach (var attr in root.Attributes())
        {
            if (!AllowedFetchAttributes.Contains(attr.Name.LocalName))
            {
                errors.Add($"Attribute '{attr.Name.LocalName}' is not allowed on <fetch>.");
            }
        }
        var entities = root.Elements("entity").ToList();
        if (entities.Count != 1)
        {
            errors.Add("<fetch> must contain exactly one <entity>.");
        }
        AddCostErrors(root, errors);
        AddOrderAliasErrors(root, errors);
        AddAggregateAliasErrors(root, errors);
        if (errors.Count > 0)
        {
            return new ValidationResult(false, errors, null);
        }

        var entityName = entities[0].Attribute("name")?.Value ?? "";
        var info = await _metadata.GetEntityAsync(entityName, ct);
        if (info is null)
        {
            return Fail($"Unknown entity '{entityName}'.");
        }

        var aliases = new Dictionary<string, EntityInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var link in entities[0].Descendants("link-entity"))
        {
            var alias = link.Attribute("alias")?.Value;
            var linkInfo = await _metadata.GetEntityAsync(link.Attribute("name")?.Value ?? "", ct);
            if (!string.IsNullOrWhiteSpace(alias) && linkInfo is not null)
            {
                aliases[alias] = linkInfo;
            }
        }
        await ValidateScopeAsync(entities[0], info, aliases, errors, ct);
        var placeholders = await CheckPlaceholdersAsync(entities[0], info, aliases, stepContext, errors, ct);
        if (stepContext?.ProducesIds == true)
        {
            await CheckProducerAsync(root, entities[0], info, errors, ct);
        }
        if (stepContext?.UsesStep is int uses && placeholders == 0)
        {
            errors.Add($"This step uses step {uses} but never uses {StepPlaceholder.Format(uses)}; filter on its ids with that placeholder.");
        }
        if (errors.Count > 0)
        {
            return new ValidationResult(false, errors, null);
        }

        ApplyTop(root);
        return new ValidationResult(true, Array.Empty<string>(), doc.ToString(SaveOptions.DisableFormatting));
    }

    private static void AddAggregateAliasErrors(XElement root, List<string> errors)
    {
        if (!string.Equals((string?)root.Attribute("aggregate"), "true", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        foreach (var attribute in root.Descendants("attribute").Where(a => string.IsNullOrEmpty((string?)a.Attribute("alias"))))
        {
            errors.Add($"In an aggregate query every attribute needs an alias; add alias=\"...\" to <attribute name=\"{(string?)attribute.Attribute("name")}\" />.");
        }
    }

    private static void AddOrderAliasErrors(XElement root, List<string> errors)
    {
        foreach (var order in root.Descendants("order"))
        {
            var alias = (string?)order.Attribute("alias");
            if (alias is null)
            {
                continue;
            }
            var local = order.Parent!.Elements("attribute").Select(a => (string?)a.Attribute("alias")).Where(a => !string.IsNullOrEmpty(a)).Select(a => a!).Distinct().ToList();
            if (local.Contains(alias, StringComparer.Ordinal))
            {
                continue;
            }
            var elsewhere = root.Descendants("attribute").Any(a => (string?)a.Attribute("alias") == alias);
            if (elsewhere)
            {
                errors.Add($"Order alias '{alias}' is defined by an attribute in a different element; the <order alias=\"{alias}\"> must sit in the same <entity> or <link-entity> element as that attribute.");
            }
            else
            {
                var existing = local.Count == 0 ? "there are none here" : string.Join(", ", local.Select(a => $"'{a}'"));
                errors.Add($"Order alias '{alias}' does not match any attribute alias in the query; use the alias of an attribute in the same element (existing aliases: {existing}).");
            }
        }
    }

    private void AddCostErrors(XElement root, List<string> errors)
    {
        var limits = _limits.Current;
        var links = root.Descendants("link-entity").ToList();
        if (links.Count > limits.MaxLinkEntities)
        {
            errors.Add($"The query has {links.Count} link-entity elements; use at most {limits.MaxLinkEntities}. Simplify the query.");
        }
        var deepest = links.Count == 0 ? 0 : links.Max(l => l.AncestorsAndSelf("link-entity").Count());
        if (deepest > limits.MaxLinkDepth)
        {
            errors.Add($"link-entity elements are nested {deepest} levels deep; use at most {limits.MaxLinkDepth}. Simplify the query.");
        }
        var conditions = root.Descendants("condition").Count();
        if (conditions > limits.MaxConditions)
        {
            errors.Add($"The query has {conditions} conditions; use at most {limits.MaxConditions}. Simplify the query.");
        }
    }

    private static readonly HashSet<string> DateGroupingUnits = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "day", "week", "month", "quarter", "year"
    };

    private static void CheckDateGrouping(XElement attribute, AttributeInfo? found, List<string> errors)
    {
        var unit = (string?)attribute.Attribute("dategrouping");
        if (unit is null)
        {
            return;
        }
        var name = (string?)attribute.Attribute("name");
        if (!DateGroupingUnits.Contains(unit))
        {
            errors.Add($"dategrouping=\"{unit}\" is not supported; use one of day, week, month, quarter, year.");
        }
        if (!string.Equals((string?)attribute.Document?.Root?.Attribute("aggregate"), "true", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("dategrouping is only allowed in an aggregate query (aggregate=\"true\" on <fetch>).");
        }
        if (!string.Equals((string?)attribute.Attribute("groupby"), "true", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"dategrouping on '{name}' needs groupby=\"true\" on the same <attribute>.");
        }
        if (found is not null && !string.Equals(found.AttributeType, "DateTime", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"dategrouping can only be used on a date field; '{name}' is not a date field.");
        }
    }

    private static ValidationResult Fail(string message)
    {
        return new ValidationResult(false, new List<string> { message }, null);
    }

    private static void ApplyTop(XElement root)
    {
        if (string.Equals(root.Attribute("aggregate")?.Value, "true", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        var ok = int.TryParse(root.Attribute("top")?.Value, out var top) && top >= 1 && top <= MaxRows;
        if (!ok)
        {
            root.SetAttributeValue("top", MaxRows);
        }
    }

    private async Task ValidateScopeAsync(XElement scope, EntityInfo info, Dictionary<string, EntityInfo> aliases,
        List<string> errors, CancellationToken ct)
    {
        foreach (var child in scope.Elements())
        {
            switch (child.Name.LocalName)
            {
                case "attribute":
                    var checkedAttribute = CheckAttribute(info, child.Attribute("name")?.Value, errors);
                    CheckDateGrouping(child, checkedAttribute, errors);
                    break;
                case "order":
                    if (child.Attribute("alias") is null || child.Attribute("attribute") is not null)
                    {
                        CheckAttribute(info, child.Attribute("attribute")?.Value, errors);
                    }
                    break;
                case "filter":
                    ValidateFilter(child, info, aliases, errors);
                    break;
                case "link-entity":
                    await ValidateLinkAsync(child, info, aliases, errors, ct);
                    break;
            }
        }
    }

    private async Task ValidateLinkAsync(XElement link, EntityInfo parent, Dictionary<string, EntityInfo> aliases,
        List<string> errors, CancellationToken ct)
    {
        var name = link.Attribute("name")?.Value ?? "";
        var linkInfo = await _metadata.GetEntityAsync(name, ct);
        if (linkInfo is null)
        {
            errors.Add($"Unknown link-entity '{name}'.");
            return;
        }
        CheckAttribute(linkInfo, link.Attribute("from")?.Value, errors);
        CheckAttribute(parent, link.Attribute("to")?.Value, errors);

        if (string.IsNullOrWhiteSpace(link.Attribute("alias")?.Value))
        {
            errors.Add($"link-entity '{name}' must have an alias.");
        }
        await ValidateScopeAsync(link, linkInfo, aliases, errors, ct);
    }

    private void ValidateFilter(XElement filter, EntityInfo info, Dictionary<string, EntityInfo> aliases, List<string> errors)
    {
        foreach (var child in filter.Elements())
        {
            if (child.Name.LocalName == "filter")
            {
                ValidateFilter(child, info, aliases, errors);
            }
            else if (child.Name.LocalName == "condition")
            {
                ValidateCondition(child, info, aliases, errors);
            }
        }
    }

    private void ValidateCondition(XElement condition, EntityInfo info, Dictionary<string, EntityInfo> aliases, List<string> errors)
    {
        if (condition.Attribute("valueof") is not null)
        {
            errors.Add("Comparing two columns (valueof) is not allowed.");
        }
        var op = condition.Attribute("operator")?.Value ?? "";
        if (!AllowedOperators.Contains(op))
        {
            errors.Add($"Operator '{op}' is not allowed.");
        }

        var target = info;
        var entityAlias = condition.Attribute("entityname")?.Value;
        if (entityAlias is not null)
        {
            if (!aliases.TryGetValue(entityAlias, out var aliasInfo))
            {
                errors.Add($"Unknown alias '{entityAlias}' in condition.");
                return;
            }
            target = aliasInfo;
        }

        var attr = CheckAttribute(target, condition.Attribute("attribute")?.Value, errors);
        if (attr is null)
        {
            return;
        }

        var values = ConditionValues(condition);
        if (AllowedOperators.Contains(op))
        {
            if (op is "between" or "not-between")
            {
                if (values.Count != 2)
                {
                    errors.Add($"Operator '{op}' needs exactly two values (two <value> elements).");
                }
            }
            else if (!NoValueOperators.Contains(op) && values.Count == 0)
            {
                errors.Add($"Operator '{op}' needs at least one value (the value attribute or <value> elements).");
            }
        }

        if (attr.AttributeType == "EntityName" && attr.LogicalName.EndsWith("idtype", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"'{attr.LogicalName}' is the hidden type companion of a lookup and cannot be filtered; to restrict the kind of record, join the related entity with a link-entity instead.");
        }

        if (attr.AttributeType == "MultiSelectPicklist")
        {
            var valid = string.Join(", ", attr.Options.Select(o => $"{o.Value}={o.Label}"));
            if (op is not ("contain-values" or "not-contain-values" or "null" or "not-null"))
            {
                errors.Add($"'{attr.LogicalName}' is a multi-select option set: use the operator contain-values (or not-contain-values) with the numeric option value. Valid: {valid}.");
            }
            else if (op is "contain-values" or "not-contain-values" && attr.Options.Count > 0)
            {
                foreach (var v in values)
                {
                    if (!(int.TryParse(v, out var n) && attr.Options.Any(o => o.Value == n)))
                    {
                        errors.Add($"Value '{v}' is not a valid option for '{attr.LogicalName}'. Valid: {valid}.");
                    }
                }
            }
        }
        else if (op is "contain-values" or "not-contain-values")
        {
            errors.Add($"Operator '{op}' only works on multi-select option set attributes; '{attr.LogicalName}' is not one.");
        }

        if (AllowedOperators.Contains(op) && !NoValueOperators.Contains(op) && attr.AttributeType is "Integer" or "BigInt" or "Decimal" or "Double" or "Money")
        {
            foreach (var v in values)
            {
                if (!decimal.TryParse(v, System.Globalization.NumberStyles.AllowLeadingSign | System.Globalization.NumberStyles.AllowDecimalPoint,
                        System.Globalization.CultureInfo.InvariantCulture, out _))
                {
                    errors.Add($"Value '{v}' for '{attr.LogicalName}' must be a number.");
                }
            }
        }

        if (op is "eq" or "ne" or "in" or "not-in")
        {
            if (attr.AttributeType is "Picklist" or "State" or "Status" or "Boolean" && attr.Options.Count > 0)
            {
                foreach (var v in values)
                {
                    var valid = (int.TryParse(v, out var n) && attr.Options.Any(o => o.Value == n))
                        || (attr.AttributeType == "Boolean" && (v.Equals("true", StringComparison.OrdinalIgnoreCase) || v.Equals("false", StringComparison.OrdinalIgnoreCase)));
                    if (!valid)
                    {
                        var options = string.Join(", ", attr.Options.Select(o => $"{o.Value}={o.Label}"));
                        errors.Add($"Value '{v}' is not a valid option for '{attr.LogicalName}'. Valid: {options}.");
                    }
                }
            }
            else if (attr.AttributeType is "Lookup" or "Customer" or "Owner" or "Uniqueidentifier")
            {
                foreach (var v in values)
                {
                    if (!Guid.TryParse(v, out _) && !StepPlaceholder.IsPlaceholder(v))
                    {
                        errors.Add($"Value '{v}' for '{attr.LogicalName}' must be a record GUID. Use only GUIDs given in the resolved or open records; never write a placeholder or a name.");
                    }
                }
            }
        }
    }

    private async Task CheckProducerAsync(XElement root, XElement rootEntity, EntityInfo rootInfo, List<string> errors, CancellationToken ct)
    {
        if (!string.Equals((string?)root.Attribute("aggregate"), "true", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        var marked = rootEntity.Descendants("attribute")
            .Where(a => (string?)a.Attribute("alias") == DistinctRewriter.RowIdAlias
                && string.Equals((string?)a.Attribute("groupby"), "true", StringComparison.OrdinalIgnoreCase)).ToList();
        if (marked.Count == 0)
        {
            errors.Add("This step's ids are used by a later step: group the aggregate by an id or lookup attribute and give it alias=\"_rowid\" with groupby=\"true\".");
            return;
        }
        foreach (var attribute in marked)
        {
            var owner = attribute.Parent!;
            var ownerInfo = owner == rootEntity ? rootInfo : await _metadata.GetEntityAsync(owner.Attribute("name")?.Value ?? "", ct);
            var found = ownerInfo?.FindAttribute((string?)attribute.Attribute("name") ?? "");
            var isId = found is not null && string.Equals(found.LogicalName, ownerInfo!.PrimaryIdAttribute, StringComparison.OrdinalIgnoreCase);
            var isLookup = found is not null && found.AttributeType is "Lookup" or "Customer" or "Owner";
            if (found is not null && !isId && !isLookup)
            {
                errors.Add($"The _rowid groupby attribute '{found.LogicalName}' must be an id or lookup field, not a plain value.");
            }
        }
    }

    private async Task<int> CheckPlaceholdersAsync(XElement scope, EntityInfo info, Dictionary<string, EntityInfo> aliases, StepContext? context,
        List<string> errors, CancellationToken ct)
    {
        var found = 0;
        foreach (var child in scope.Elements())
        {
            if (child.Name.LocalName == "filter")
            {
                foreach (var condition in child.Descendants("condition"))
                {
                    found += CheckPlaceholderCondition(condition, info, aliases, context, errors);
                }
            }
            else if (child.Name.LocalName == "link-entity")
            {
                var linkInfo = await _metadata.GetEntityAsync(child.Attribute("name")?.Value ?? "", ct);
                if (linkInfo is not null)
                {
                    found += await CheckPlaceholdersAsync(child, linkInfo, aliases, context, errors, ct);
                }
            }
        }
        return found;
    }

    private static int CheckPlaceholderCondition(XElement condition, EntityInfo info, Dictionary<string, EntityInfo> aliases, StepContext? context, List<string> errors)
    {
        var values = ConditionValues(condition);
        var used = values.Where(StepPlaceholder.IsPlaceholder).ToList();
        if (used.Count == 0)
        {
            return 0;
        }

        if (context?.UsesStep is not int uses)
        {
            errors.Add("A step placeholder ({{stepN}}) is only allowed in a step that uses an earlier step.");
            return used.Count;
        }
        foreach (var value in used)
        {
            StepPlaceholder.TryParse(value, out var n);
            if (n != uses || !context.PreviousIdEntities.ContainsKey(n))
            {
                errors.Add($"Placeholder {value} refers to a step this step does not use; it may only use step {uses}.");
                return used.Count;
            }
        }
        if (values.Count != 1)
        {
            errors.Add("A step placeholder must be the only value of its condition.");
            return used.Count;
        }
        var op = condition.Attribute("operator")?.Value ?? "";
        if (op is not ("eq" or "in"))
        {
            errors.Add("A step placeholder may only be used with the operators eq or in.");
            return used.Count;
        }

        var target = info;
        var alias = condition.Attribute("entityname")?.Value;
        if (alias is not null && !aliases.TryGetValue(alias, out target!))
        {
            return used.Count;
        }
        var attr = target.FindAttribute(condition.Attribute("attribute")?.Value ?? "");
        if (attr is null)
        {
            return used.Count;
        }

        var previous = context.PreviousIdEntities[uses];
        if (previous.Count == 0)
        {
            errors.Add($"Cannot verify which records the earlier step {uses} produces, so its ids cannot be used for '{attr.LogicalName}'.");
            return used.Count;
        }
        IReadOnlyCollection<string> targets;
        if (string.Equals(attr.LogicalName, target.PrimaryIdAttribute, StringComparison.OrdinalIgnoreCase))
        {
            targets = new[] { target.LogicalName };
        }
        else if (attr.AttributeType is "Lookup" or "Customer" or "Owner")
        {
            targets = target.Relationships
                .Where(r => string.Equals(r.ReferencingEntity, target.LogicalName, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(r.ReferencingAttribute, attr.LogicalName, StringComparison.OrdinalIgnoreCase))
                .Select(r => r.ReferencedEntity).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (targets.Count == 0)
            {
                errors.Add($"Cannot verify which records '{attr.LogicalName}' points to; a step placeholder cannot be used on it.");
                return used.Count;
            }
        }
        else
        {
            errors.Add($"A step placeholder can only be used on an id or lookup field, not on '{attr.LogicalName}'.");
            return used.Count;
        }

        if (!previous.Intersect(targets, StringComparer.OrdinalIgnoreCase).Any())
        {
            errors.Add($"The ids of '{string.Join("' or '", previous)}' cannot be used for '{attr.LogicalName}', which points to {string.Join(" or ", targets)}.");
        }
        return used.Count;
    }

    private static List<string> ConditionValues(XElement condition)
    {
        var values = condition.Elements("value").Select(v => v.Value).ToList();
        var single = condition.Attribute("value")?.Value;
        if (single is not null)
        {
            values.Add(single);
        }
        return values;
    }

    private static AttributeInfo? CheckAttribute(EntityInfo info, string? name, List<string> errors)
    {
        var attr = string.IsNullOrEmpty(name) ? null : info.FindAttribute(name);
        if (attr is null)
        {
            errors.Add($"Attribute '{name}' does not exist on entity '{info.LogicalName}'.");
        }
        return attr;
    }
}
