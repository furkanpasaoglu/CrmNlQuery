using CrmNlQuery.Application.Access.Models;
using CrmNlQuery.Application.Access.Services;
using CrmNlQuery.Application.Localization.Services;

namespace CrmNlQuery.Tests.Application.Localization;

/// <summary>
/// Tests that admin API messages come in the request language.
/// </summary>
[Collection("RequestLanguage")]
public class AdminMessagesLocalizationTests
{
    /// <summary>
    /// Verifies that the group mapping risk message follows the request language and fills in the group.
    /// </summary>
    [Fact]
    public void GroupMappingRiskMessage_FollowsTheRequestLanguage_AndFillsTheGroup()
    {
        try
        {
            RequestLanguage.Set("en");
            var en = GroupMappingRisk.Message(GroupMappingRiskKind.AdministratorRole, @"LAB\Staff");
            Assert.StartsWith(@"Everyone in LAB\Staff would become an administrator", en);

            RequestLanguage.Set("tr");
            var tr = GroupMappingRisk.Message(GroupMappingRiskKind.AdministratorRole, @"LAB\Staff");
            Assert.StartsWith(@"LAB\Staff grubundaki herkes yönetici olur", tr);
            Assert.DoesNotContain("{", tr);
        }
        finally
        {
            RequestLanguage.Set("en");
        }
    }
}
