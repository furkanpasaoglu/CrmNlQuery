namespace CrmNlQuery.Application.Drafts.Models;

/// <summary>
/// Holds the fixed catalog of draft placeholders the model may name but never sees values for.
/// </summary>
public static class DraftPlaceholders
{
    public const string CustomerName = "customer_name";
    public const string ContactFirstName = "contact_first_name";
    public const string ContactFullName = "contact_full_name";
    public const string SenderName = "sender_name";
    public const string OpenOpportunityCount = "open_opportunity_count";
    public const string OpenOpportunityTotal = "open_opportunity_total";
    public const string NearestContractEnd = "nearest_contract_end";
    public const string OpenCaseCount = "open_case_count";
    public const string LastActivityDate = "last_activity_date";
    public const string Today = "today";

    public static readonly IReadOnlyList<DraftPlaceholder> All = new List<DraftPlaceholder>
    {
        new DraftPlaceholder(CustomerName, "customer name", "the customer company name"),
        new DraftPlaceholder(ContactFirstName, "contact first name", "first name of the person being written to (use in the greeting)"),
        new DraftPlaceholder(ContactFullName, "contact full name", "full name of the person being written to"),
        new DraftPlaceholder(SenderName, "your name", "the sender's name (use in the signature)"),
        new DraftPlaceholder(OpenOpportunityCount, "number of open opportunities", "number of open opportunities with this customer"),
        new DraftPlaceholder(OpenOpportunityTotal, "open opportunities total", "total value of the open opportunities"),
        new DraftPlaceholder(NearestContractEnd, "contract end date", "end date of the contract that ends soonest"),
        new DraftPlaceholder(OpenCaseCount, "number of open cases", "number of open support cases"),
        new DraftPlaceholder(LastActivityDate, "last contact date", "date of the last completed contact with this customer"),
        new DraftPlaceholder(Today, "today's date", "today's date"),
    };

    /// <summary>
    /// Finds a placeholder in the catalog by name, or null.
    /// </summary>
    public static DraftPlaceholder? Find(string name)
        => All.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal));
}
