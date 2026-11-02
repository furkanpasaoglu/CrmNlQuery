namespace CrmNlQuery.Application.Setup.Models;

/// <summary>
/// The model server typed into the wizard, used to test it before anything is saved.
/// </summary>
public sealed record ModelProbeInput(string Provider, string BaseUrl, string Model, string? ApiKey);
