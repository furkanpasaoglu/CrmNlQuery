using CrmNlQuery.Domain.Common.Services;
using CrmNlQuery.Domain.Metadata.Entities;

namespace CrmNlQuery.Domain.Metadata.Services;

/// <summary>
/// Matches a user's word to an entity attribute by name or label.
/// </summary>
public static class AttributeMatcher
{
    /// <summary>
    /// Matches a token to an attribute of the entity.
    /// </summary>
    public static MatchResult Match(string token, EntityInfo entity)
    {
        var key = Key(token);
        if (key.Length == 0)
        {
            return new MatchResult(null, new List<AttributeInfo>());
        }
        if (key == "id")
        {
            return Found(entity.FindAttribute(entity.PrimaryIdAttribute));
        }
        if (key == "name")
        {
            return Found(entity.FindAttribute(entity.PrimaryNameAttribute));
        }

        var exact = entity.Attributes.FirstOrDefault(a => Key(a.LogicalName) == key || Key(a.Label) == key);
        if (exact is not null)
        {
            return new MatchResult(exact, new List<AttributeInfo>());
        }

        var suggestions = entity.Attributes
            .Where(a => IsRelated(key, Key(a.LogicalName)) || IsRelated(key, Key(a.Label)))
            .OrderBy(a => Math.Abs(a.LogicalName.Length - token.Length))
            .Take(5)
            .ToList();
        return new MatchResult(null, suggestions);
    }

    private static MatchResult Found(AttributeInfo? attribute)
    {
        return new MatchResult(attribute, new List<AttributeInfo>());
    }

    private static bool IsRelated(string a, string b)
    {
        if (a.Length < 3 || b.Length < 3)
        {
            return false;
        }
        return a.Contains(b, StringComparison.Ordinal) || b.Contains(a, StringComparison.Ordinal);
    }

    private static string Key(string text)
    {
        return new string(NameNormalizer.Fold(text).Where(char.IsLetterOrDigit).ToArray());
    }
}
