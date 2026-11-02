using CrmNlQuery.Application.Access.Models;
using CrmNlQuery.Application.Localization.Models;
using CrmNlQuery.Application.Localization.Services;
using CrmNlQuery.Domain.Access.Constants;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Domain.Common.Services;

namespace CrmNlQuery.Application.Access.Services;

/// <summary>
/// Assesses whether an AD group mapping needs server-side confirmation, such as Administrator or a broad group with management rights.
/// </summary>
public static class GroupMappingRisk
{
    private static readonly HashSet<string> BroadGroups = new HashSet<string>(StringComparer.Ordinal)
    {
        NameNormalizer.Fold("Domain Users"),
        NameNormalizer.Fold("Everyone"),
        NameNormalizer.Fold("Authenticated Users"),
        NameNormalizer.Fold("Users"),
        NameNormalizer.Fold("Domain Computers"),
        NameNormalizer.Fold("Etki Alanı Kullanıcıları"),
        NameNormalizer.Fold("Herkes"),
        NameNormalizer.Fold("Kimliği Doğrulanmış Kullanıcılar"),
    };

    /// <summary>
    /// Returns whether a group name is a broad group such as Domain Users or Everyone.
    /// </summary>
    public static bool IsBroadGroup(string? group)
    {
        var name = (group ?? "").Trim();
        var slash = name.LastIndexOf('\\');
        var tail = slash >= 0 ? name.Substring(slash + 1) : name;
        return BroadGroups.Contains(NameNormalizer.Fold(tail));
    }

    /// <summary>
    /// Assesses the risk of newly added roles for a group; already confirmed roles are not reassessed.
    /// </summary>
    public static GroupMappingRiskKind Assess(string group, IEnumerable<AccessRole> addedRoles)
    {
        var roles = addedRoles.ToList();
        if (roles.Any(r => r.Builtin))
        {
            return GroupMappingRiskKind.AdministratorRole;
        }
        if (IsBroadGroup(group) && roles.Any(r => r.Permissions.Any(p => p != Permissions.AppUse)))
        {
            return GroupMappingRiskKind.BroadGroupWithAdministrationRights;
        }
        return GroupMappingRiskKind.None;
    }

    /// <summary>
    /// Builds the confirmation message for a risk kind and group.
    /// </summary>
    public static string Message(GroupMappingRiskKind kind, string group) => kind switch
    {
        GroupMappingRiskKind.AdministratorRole =>
            MessageCatalog.Now(Msg.AdmRiskAdmin, MessageCatalog.Args(("group", group))),
        GroupMappingRiskKind.BroadGroupWithAdministrationRights =>
            MessageCatalog.Now(Msg.AdmRiskBroad, MessageCatalog.Args(("group", group))),
        _ => "",
    };

    /// <summary>
    /// Returns the audit note text recorded when a risky mapping is confirmed.
    /// </summary>
    public static string AuditNote(GroupMappingRiskKind kind) => kind switch
    {
        GroupMappingRiskKind.AdministratorRole => "CONFIRMED administrator grant",
        GroupMappingRiskKind.BroadGroupWithAdministrationRights => "CONFIRMED broad-group grant",
        _ => "",
    };
}
