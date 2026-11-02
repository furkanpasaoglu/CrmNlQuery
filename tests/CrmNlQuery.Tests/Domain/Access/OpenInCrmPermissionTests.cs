using CrmNlQuery.Domain.Access.Constants;

namespace CrmNlQuery.Tests.Domain.Access;

/// <summary>
/// Holds tests for the Open in CRM permission.
/// </summary>
public class OpenInCrmPermissionTests
{
    /// <summary>
    /// Verifies that records open in crm is aKnown permission in the application group.
    /// </summary>
    [Fact]
    public void Records_OpenInCrm_IsAKnownPermissionInTheApplicationGroup()
    {
        Assert.True(Permissions.IsKnown("Records.OpenInCrm"));
        var info = Assert.Single(Permissions.All, p => p.Key == Permissions.RecordsOpenInCrm);
        Assert.Equal("Application", info.Group);
        Assert.Contains("Features:OpenInCrm", info.Description);
    }

    /// <summary>
    /// Verifies that it is aSeparate permission from creating records.
    /// </summary>
    [Fact]
    public void ItIsASeparatePermissionFromCreatingRecords()
    {
        Assert.NotEqual(Permissions.RecordsCreate, Permissions.RecordsOpenInCrm);
        Assert.Equal(Permissions.All.Count, Permissions.All.Select(p => p.Key).Distinct().Count());
    }
}
