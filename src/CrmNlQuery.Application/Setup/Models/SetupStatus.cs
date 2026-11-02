namespace CrmNlQuery.Application.Setup.Models;

/// <summary>
/// What the wizard page needs to know first: whether setup is needed, whether it is locked after wrong codes and whether the database and the key work.
/// </summary>
public sealed record SetupStatus(bool NeedsSetup, bool Locked, bool DatabaseAvailable, bool KeyProblem);
