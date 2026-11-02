namespace CrmNlQuery.Domain.Glossary.Constants;

/// <summary>
/// Holds the comparison operators allowed in glossary conditions.
/// </summary>
public static class GlossaryOperator
{
    public const string Eq = "eq";
    public const string Ne = "ne";
    public const string In = "in";
    public const string NotIn = "not-in";
    public const string Gt = "gt";
    public const string Ge = "ge";
    public const string Lt = "lt";
    public const string Le = "le";
    public const string Like = "like";
    public const string Null = "null";
    public const string NotNull = "not-null";

    public static readonly IReadOnlyList<string> All = new[] { Eq, Ne, In, NotIn, Gt, Ge, Lt, Le, Like, Null, NotNull };

    /// <summary>
    /// Tells whether the operator takes no value.
    /// </summary>
    public static bool NeedsNoValue(string op) => op is Null or NotNull;
    /// <summary>
    /// Tells whether the operator takes multiple values.
    /// </summary>
    public static bool NeedsManyValues(string op) => op is In or NotIn;
}
