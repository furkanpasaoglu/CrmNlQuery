using CrmNlQuery.Domain.Metadata.Entities;

namespace CrmNlQuery.Application.Design.Models;

/// <summary>
/// Context the design validator uses to check a design without touching the CRM.
/// </summary>
public sealed class DesignContext
{
    public string Prefix { get; init; } = "new";
    public IReadOnlySet<string> ExistingEntities { get; init; } = new HashSet<string>();
    public IReadOnlyDictionary<string, EntityInfo> Detailed { get; init; } = new Dictionary<string, EntityInfo>();
    public IReadOnlySet<string> CandidateNames { get; init; } = new HashSet<string>();
}
