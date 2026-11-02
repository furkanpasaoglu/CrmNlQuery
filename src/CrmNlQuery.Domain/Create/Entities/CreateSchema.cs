using CrmNlQuery.Domain.Common.Services;

namespace CrmNlQuery.Domain.Create.Entities;

/// <summary>
/// Describes the writable fields of one table for record creation.
/// </summary>
public sealed record CreateSchema(
    string Entity,
    string EntitySet,
    string Label,
    string PrimaryIdAttribute,
    string PrimaryNameAttribute,
    IReadOnlyList<CreateField> Fields)
{
    /// <summary>
    /// Finds a field by logical name or label, ignoring case, Turkish letters and punctuation.
    /// </summary>
    public CreateField? FindField(string key)
    {
        var wanted = Key(key);
        if (wanted.Length == 0)
        {
            return null;
        }
        return Fields.FirstOrDefault(f => Key(f.Name) == wanted) ?? Fields.FirstOrDefault(f => Key(f.Label) == wanted);
    }

    /// <summary>
    /// Normalizes a name into the folded key used to match fields.
    /// </summary>
    public static string Key(string text)
    {
        var chars = NameNormalizer.Fold(text).ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (!char.IsLetterOrDigit(chars[i]))
            {
                chars[i] = ' ';
            }
        }
        return string.Join(" ", new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
