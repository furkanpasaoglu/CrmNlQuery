namespace CrmNlQuery.Web.Contracts.Setup;

/// <summary>
/// What the wizard page needs to know before the first step: whether setup is needed, whether the code is locked and whether the database can be reached.
/// </summary>
public sealed record SetupStateDto(bool NeedsSetup, bool Locked, bool DatabaseAvailable);
