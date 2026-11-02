using System.Text.RegularExpressions;
using CrmNlQuery.Application.Design.Models;
using CrmNlQuery.Domain.Metadata.Entities;

namespace CrmNlQuery.Application.Design.Services;

/// <summary>
/// Validates a customization design against naming, conflict, type, relationship and view rules without touching the CRM.
/// </summary>
public static class DesignValidator
{
    public const int MaxNameLength = 50;

    private static readonly Regex NamePattern = new Regex("^[a-z][a-z0-9_]*$", RegexOptions.Compiled);
    private static readonly HashSet<string> FieldTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "string", "memo", "integer", "decimal", "money", "datetime", "date", "boolean", "picklist", "lookup"
    };
    private static readonly HashSet<string> Kinds = new HashSet<string>(StringComparer.Ordinal) { "one-to-many", "many-to-one", "many-to-many" };
    private static readonly HashSet<string> DeleteBehaviors = new HashSet<string>(StringComparer.Ordinal) { "cascade", "restrict", "remove-link", "none" };
    private static readonly HashSet<string> Actions = new HashSet<string>(StringComparer.Ordinal) { "extend", "reuse", "ignore" };

    /// <summary>
    /// Validates reuse decisions, allowing references only to candidate tables.
    /// </summary>
    public static IReadOnlyList<string> ValidateDecisions(IReadOnlyList<ReuseDecision> decisions, DesignContext context)
    {
        var errors = new List<string>();
        foreach (var d in decisions)
        {
            if (!context.CandidateNames.Contains(d.Entity))
            {
                errors.Add($"Decision for '{d.Entity}': it is not one of the candidate tables; decide only about the candidates.");
            }
            if (!Actions.Contains(d.Action))
            {
                errors.Add($"Decision for '{d.Entity}': action '{d.Action}' is not allowed; use extend, reuse or ignore.");
            }
        }
        return errors;
    }

    /// <summary>
    /// Validates a design document and returns the problems to feed back to the model.
    /// </summary>
    public static IReadOnlyList<string> Validate(DesignDocument doc, DesignContext context)
    {
        var errors = new List<string>();
        var prefix = context.Prefix + "_";
        if (doc.Tables.Count == 0)
        {
            errors.Add("The design has no tables; list at least one new or extended table.");
        }

        var designTables = new Dictionary<string, DesignTable>(StringComparer.Ordinal);
        foreach (var table in doc.Tables)
        {
            var name = table.LogicalName ?? "";
            if (designTables.ContainsKey(name))
            {
                errors.Add($"Table '{name}' is listed twice.");
                continue;
            }
            designTables[name] = table;
        }

        foreach (var table in doc.Tables)
        {
            CheckTable(table, context, prefix, errors);
        }

        var allEntities = new HashSet<string>(context.ExistingEntities, StringComparer.Ordinal);
        foreach (var name in designTables.Keys)
        {
            allEntities.Add(name);
        }

        foreach (var table in doc.Tables)
        {
            foreach (var field in table.Fields.Where(f => f.Type == "lookup"))
            {
                if (string.IsNullOrWhiteSpace(field.Target) || !allEntities.Contains(field.Target))
                {
                    errors.Add($"Field '{field.LogicalName}' of '{table.LogicalName}' is a lookup to '{field.Target}', which is neither in the CRM nor in this design.");
                }
            }
        }

        foreach (var r in doc.Relationships)
        {
            CheckRelationship(r, designTables, allEntities, errors);
        }
        foreach (var v in doc.Views)
        {
            CheckView(v, designTables, allEntities, context, errors);
        }
        return errors;
    }

    private static void CheckTable(DesignTable table, DesignContext context, string prefix, List<string> errors)
    {
        var name = table.LogicalName ?? "";
        var extends = !string.IsNullOrWhiteSpace(table.Extends);
        if (!NamePattern.IsMatch(name) || name.Length > MaxNameLength)
        {
            errors.Add($"Table logical name '{name}' is not valid: use lower-case letters, digits and underscores, at most {MaxNameLength} characters.");
        }
        if (string.IsNullOrWhiteSpace(table.DisplayName))
        {
            errors.Add($"Table '{name}' has no display name.");
        }

        if (extends)
        {
            if (!string.Equals(table.Extends, name, StringComparison.Ordinal))
            {
                errors.Add($"Table '{name}' extends '{table.Extends}': for an extension the logical name must be the existing table's name.");
            }
            if (!context.CandidateNames.Contains(table.Extends) || !context.ExistingEntities.Contains(table.Extends))
            {
                errors.Add($"Table '{name}' extends '{table.Extends}', which is not one of the existing candidate tables.");
            }
        }
        else
        {
            if (!name.StartsWith(prefix, StringComparison.Ordinal))
            {
                errors.Add($"New table '{name}' must start with the publisher prefix '{prefix}'.");
            }
            if (context.ExistingEntities.Contains(name))
            {
                errors.Add($"New table '{name}' already exists in the CRM. To extend it, set extends to '{name}' and list only NEW fields (their names start with '{prefix}'); otherwise choose another name.");
            }
            if (table.Ownership != "user" && table.Ownership != "organization")
            {
                errors.Add($"Table '{name}': ownership must be user or organization.");
            }
            var primary = table.PrimaryNameLogical ?? "";
            if (!NamePattern.IsMatch(primary) || !primary.StartsWith(prefix, StringComparison.Ordinal) || primary.Length > MaxNameLength || string.IsNullOrWhiteSpace(table.PrimaryNameDisplay))
            {
                errors.Add($"New table '{name}' needs a primary name field: primaryNameLogical starting with '{prefix}' and a primaryNameDisplay.");
            }
        }

        context.Detailed.TryGetValue(name, out var existing);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in table.Fields)
        {
            CheckField(table, field, prefix, existing, seen, errors);
        }
        if (!string.IsNullOrEmpty(table.PrimaryNameLogical) && seen.Contains(table.PrimaryNameLogical))
        {
            errors.Add($"Table '{name}': the primary name field '{table.PrimaryNameLogical}' must not be repeated in fields.");
        }
    }

    private static void CheckField(DesignTable table, DesignField field, string prefix, EntityInfo? existing, HashSet<string> seen, List<string> errors)
    {
        var name = field.LogicalName ?? "";
        var where = $"Field '{name}' of '{table.LogicalName}'";
        if (!NamePattern.IsMatch(name) || !name.StartsWith(prefix, StringComparison.Ordinal) || name.Length > MaxNameLength)
        {
            errors.Add($"{where}: the logical name must start with '{prefix}', use lower-case letters, digits and underscores, at most {MaxNameLength} characters.");
        }
        if (!seen.Add(name))
        {
            errors.Add($"{where} is listed twice.");
        }
        if (existing is not null && existing.FindAttribute(name) is not null)
        {
            errors.Add($"{where} already exists on the table; list only NEW fields.");
        }
        if (string.IsNullOrWhiteSpace(field.DisplayName))
        {
            errors.Add($"{where} has no display name.");
        }
        if (!FieldTypes.Contains(field.Type))
        {
            errors.Add($"{where}: type '{field.Type}' is not allowed; use string, memo, integer, decimal, money, datetime, date, boolean, picklist or lookup.");
        }
        if (field.Type == "string" && field.Length is < 0 or > 4000)
        {
            errors.Add($"{where}: a string length must be between 1 and 4000.");
        }
        if (field.Type == "picklist")
        {
            if (field.Options.Count < 2)
            {
                errors.Add($"{where}: a picklist needs at least two options.");
            }
            if (field.Options.Select(o => o.Value).Distinct().Count() != field.Options.Count || field.Options.Select(o => o.Label.Trim().ToLowerInvariant()).Distinct().Count() != field.Options.Count)
            {
                errors.Add($"{where}: option values and labels must be unique.");
            }
            if (field.Options.Any(o => string.IsNullOrWhiteSpace(o.Label)))
            {
                errors.Add($"{where}: every option needs a label.");
            }
        }
    }

    private static void CheckRelationship(DesignRelationship r, Dictionary<string, DesignTable> designTables, HashSet<string> allEntities, List<string> errors)
    {
        var label = $"Relationship {r.From} -> {r.To}";
        if (!Kinds.Contains(r.Kind))
        {
            errors.Add($"{label}: kind '{r.Kind}' is not allowed; use one-to-many, many-to-one or many-to-many.");
            return;
        }
        if (!allEntities.Contains(r.From) || !allEntities.Contains(r.To))
        {
            errors.Add($"{label}: both tables must exist in the CRM or in this design.");
            return;
        }
        if (r.Kind == "many-to-many")
        {
            if (!string.IsNullOrWhiteSpace(r.LookupField))
            {
                errors.Add($"{label}: a many-to-many relationship has no lookup field (it uses an intersect table); leave lookupField empty.");
            }
            return;
        }
        if (!DeleteBehaviors.Contains(r.DeleteBehavior))
        {
            errors.Add($"{label}: deleteBehavior '{r.DeleteBehavior}' is not allowed; use cascade, restrict, remove-link or none.");
        }
        var many = r.Kind == "one-to-many" ? r.To : r.From;
        var one = r.Kind == "one-to-many" ? r.From : r.To;
        if (!designTables.TryGetValue(many, out var manyTable))
        {
            errors.Add($"{label}: the lookup field lives on '{many}', so list '{many}' in tables (as an extension with the new lookup field).");
            return;
        }
        var lookup = manyTable.Fields.FirstOrDefault(f => f.LogicalName == r.LookupField);
        if (lookup is null || lookup.Type != "lookup")
        {
            errors.Add($"{label}: lookupField '{r.LookupField}' must be a lookup field of '{many}'.");
        }
        else if (!string.Equals(lookup.Target, one, StringComparison.Ordinal))
        {
            errors.Add($"{label}: lookup '{r.LookupField}' points to '{lookup.Target}', not to '{one}'.");
        }
    }

    private static void CheckView(DesignView v, Dictionary<string, DesignTable> designTables, HashSet<string> allEntities, DesignContext context, List<string> errors)
    {
        if (!allEntities.Contains(v.Table))
        {
            errors.Add($"View '{v.Name}': table '{v.Table}' is neither in the CRM nor in this design.");
            return;
        }
        if (v.Columns.Count == 0)
        {
            errors.Add($"View '{v.Name}' has no columns.");
        }
        designTables.TryGetValue(v.Table, out var designTable);
        context.Detailed.TryGetValue(v.Table, out var existing);
        foreach (var column in v.Columns)
        {
            var known = (designTable is not null && (designTable.PrimaryNameLogical == column || designTable.Fields.Any(f => f.LogicalName == column)))
                || (existing is not null && existing.FindAttribute(column) is not null);
            var unknowable = designTable is null && existing is null;
            if (!known && !unknowable)
            {
                errors.Add($"View '{v.Name}': column '{column}' does not exist on '{v.Table}'.");
            }
        }
    }
}
