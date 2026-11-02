using System.Text.RegularExpressions;
using CrmNlQuery.Domain.Common.Services;

namespace CrmNlQuery.Application.DataPolicy.Services;

/// <summary>
/// Identifies field names that look sensitive such as national id, IBAN, salary, credit and birth date, which are closed by default.
/// </summary>
public static class SensitiveNames
{
    private static readonly string[] Contains = { "kimlik", "iban", "maas", "kredi", "dogum", "salary", "passport", "birthdate", "governmentid", "nationalid", "taxid" };

    private static readonly HashSet<string> Tokens = new HashSet<string>(StringComparer.Ordinal) { "tc", "tckn", "tcno", "tcid", "ssn" };

    /// <summary>
    /// Checks whether a field's logical name or label looks sensitive.
    /// </summary>
    public static bool IsSensitive(string logicalName, string? label)
    {
        return Check(logicalName) || (!string.IsNullOrWhiteSpace(label) && Check(label!));
    }

    private static bool Check(string text)
    {
        var folded = NameNormalizer.Fold(text).Replace(".", "");
        var compact = folded.Replace(" ", "");
        foreach (var word in Contains)
        {
            if (folded.Contains(word, StringComparison.Ordinal) || compact.Contains(word, StringComparison.Ordinal))
            {
                return true;
            }
        }
        foreach (var token in Regex.Split(folded, "[^a-z0-9]+"))
        {
            if (token.Length > 0 && Tokens.Contains(token))
            {
                return true;
            }
        }
        return false;
    }
}
