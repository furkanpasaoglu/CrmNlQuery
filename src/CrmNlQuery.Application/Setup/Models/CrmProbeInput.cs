namespace CrmNlQuery.Application.Setup.Models;

/// <summary>
/// The CRM connection typed into the wizard, used to test the connection before anything is saved.
/// </summary>
public sealed record CrmProbeInput(string BaseUrl, string Domain, string Username, string Password);
