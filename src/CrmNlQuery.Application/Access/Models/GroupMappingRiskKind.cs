namespace CrmNlQuery.Application.Access.Models;

/// <summary>
/// Kinds of risk a group-to-role mapping can carry.
/// </summary>
public enum GroupMappingRiskKind
{
    None,
    AdministratorRole,
    BroadGroupWithAdministrationRights,
}
