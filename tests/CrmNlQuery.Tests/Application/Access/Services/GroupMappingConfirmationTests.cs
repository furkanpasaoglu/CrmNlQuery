using CrmNlQuery.Application.Access.Services;
using CrmNlQuery.Domain.Access.Constants;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Infrastructure.Persistence.Memory.Access;
using CrmNlQuery.Application.Access.Models;

namespace CrmNlQuery.Tests.Application.Access.Services;

/// <summary>
/// Tests that mapping a group to Administrator, or a broad group to a management permission, requires server-side confirmation.
/// </summary>
public class GroupMappingConfirmationTests
{
    private const string Boss = @"LAB\boss";

    private static async Task<(AccessService Service, AccessPrincipal Admin)> MakeAsync()
    {
        var service = new AccessService(new MemoryAccessStore(), new[] { Boss });
        await service.EnsureBootstrapAsync();
        return (service, (await service.ResolveAsync(Boss))!);
    }

    private static async Task<Guid> RoleAsync(AccessService service, AccessPrincipal admin, string name, params string[] permissions)
        => (await service.CreateRoleAsync(admin, name, permissions)).Id!.Value;

    /// <summary>
    /// Verifies that mapping a group to administrator needs confirmation and saves nothing without.
    /// </summary>
    [Fact]
    public async Task MappingAGroupToAdministrator_NeedsConfirmation_AndSavesNothingWithout()
    {
        var (service, admin) = await MakeAsync();

        var result = await service.SetGroupRolesAsync(admin, @"LAB\Staff", new[] { AccessRoles.AdministratorId });

        Assert.Equal(AccessError.ConfirmationRequired, result.Error);
        Assert.Contains("administrator", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(@"LAB\Staff", result.Message);
        Assert.Empty(await service.ListGroupMappingsAsync());
        Assert.DoesNotContain(await service.ListAuditAsync(20), a => a.Action == "group.roles");
    }

    /// <summary>
    /// Verifies that with confirmation the mapping is saved and the audit record says it was confirmed.
    /// </summary>
    [Fact]
    public async Task WithConfirmation_TheMappingIsSaved_AndTheAuditRecordSaysItWasConfirmed()
    {
        var (service, admin) = await MakeAsync();

        var result = await service.SetGroupRolesAsync(admin, @"LAB\Staff", new[] { AccessRoles.AdministratorId }, default, confirmed: true);

        Assert.True(result.Ok);
        Assert.Single(await service.ListGroupMappingsAsync());
        var entry = (await service.ListAuditAsync(20)).First(a => a.Action == "group.roles");
        Assert.Contains("CONFIRMED administrator grant", entry.Detail);
    }

    /// <summary>
    /// Verifies that a broad group with an administration role needs confirmation.
    /// </summary>
    [Theory]
    [InlineData(@"LAB\Domain Users")]
    [InlineData(@"LAB\domain users")]
    [InlineData(@"NT AUTHORITY\Authenticated Users")]
    [InlineData(@"BUILTIN\Users")]
    [InlineData(@"CONTOSO\Etki Alanı Kullanıcıları")]
    public async Task ABroadGroup_WithAnAdministrationRole_NeedsConfirmation(string group)
    {
        var (service, admin) = await MakeAsync();
        var role = await RoleAsync(service, admin, "Viewer", Permissions.DashboardView);

        var result = await service.SetGroupRolesAsync(admin, group, new[] { role });

        Assert.Equal(AccessError.ConfirmationRequired, result.Error);
        Assert.Empty(await service.ListGroupMappingsAsync());
        Assert.True((await service.SetGroupRolesAsync(admin, group, new[] { role }, default, confirmed: true)).Ok);
        Assert.Contains("CONFIRMED broad-group grant", (await service.ListAuditAsync(20)).First(a => a.Action == "group.roles").Detail);
    }

    /// <summary>
    /// Verifies that a broad group with only the app use role needs no confirmation.
    /// </summary>
    [Fact]
    public async Task ABroadGroup_WithOnlyTheAppUseRole_NeedsNoConfirmation()
    {
        var (service, admin) = await MakeAsync();
        var role = await RoleAsync(service, admin, "Users", Permissions.AppUse);

        Assert.True((await service.SetGroupRolesAsync(admin, @"LAB\Domain Users", new[] { role })).Ok);
    }

    /// <summary>
    /// Verifies that an ordinary group with a non administrator role needs no confirmation.
    /// </summary>
    [Fact]
    public async Task AnOrdinaryGroup_WithANonAdministratorRole_NeedsNoConfirmation()
    {
        var (service, admin) = await MakeAsync();
        var role = await RoleAsync(service, admin, "Analysts", Permissions.DashboardView, Permissions.LogsView);

        Assert.True((await service.SetGroupRolesAsync(admin, @"LAB\Analysts", new[] { role })).Ok);
    }

    /// <summary>
    /// Verifies that only newly added roles are assessed removing or keeping the administrator role needs no reconfirmation.
    /// </summary>
    [Fact]
    public async Task OnlyNewlyAddedRoles_AreAssessed_RemovingOrKeepingTheAdministratorRoleNeedsNoReconfirmation()
    {
        var (service, admin) = await MakeAsync();
        var harmless = await RoleAsync(service, admin, "Users", Permissions.AppUse);
        await service.SetGroupRolesAsync(admin, @"LAB\Staff", new[] { AccessRoles.AdministratorId }, default, confirmed: true);

        Assert.True((await service.SetGroupRolesAsync(admin, @"LAB\Staff", new[] { AccessRoles.AdministratorId, harmless })).Ok);
        Assert.True((await service.SetGroupRolesAsync(admin, @"LAB\Staff", new[] { harmless })).Ok);
        Assert.True((await service.SetGroupRolesAsync(admin, @"LAB\Staff", new Guid[0])).Ok);
    }

    /// <summary>
    /// Verifies that adding the administrator role to an existing mapping needs confirmation.
    /// </summary>
    [Fact]
    public async Task AddingTheAdministratorRoleToAnExistingMapping_NeedsConfirmation()
    {
        var (service, admin) = await MakeAsync();
        var harmless = await RoleAsync(service, admin, "Users", Permissions.AppUse);
        await service.SetGroupRolesAsync(admin, @"LAB\Staff", new[] { harmless });

        var result = await service.SetGroupRolesAsync(admin, @"LAB\Staff", new[] { harmless, AccessRoles.AdministratorId });

        Assert.Equal(AccessError.ConfirmationRequired, result.Error);
        Assert.Equal(new[] { harmless }, Assert.Single(await service.ListGroupMappingsAsync()).RoleIds);
    }

    /// <summary>
    /// Verifies that the broad group list is known.
    /// </summary>
    [Theory]
    [InlineData(@"LAB\Domain Users", true)]
    [InlineData(@"LAB\Sales", false)]
    [InlineData(@"Everyone", true)]
    [InlineData(@"LAB\Domain Admins", false)]
    public void TheBroadGroupList_IsKnown(string group, bool broad)
    {
        Assert.Equal(broad, GroupMappingRisk.IsBroadGroup(group));
    }
}
