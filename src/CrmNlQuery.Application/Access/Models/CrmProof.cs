namespace CrmNlQuery.Application.Access.Models;

/// <summary>
/// The CRM record a user created to prove who they are: who created it, whether it was created on behalf of another user and whether it was changed after it was created.
/// </summary>
public sealed record CrmProof(string RecordId, Guid? CreatedByUserId, bool CreatedOnBehalf, bool ModifiedAfterCreation);
