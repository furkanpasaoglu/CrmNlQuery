namespace CrmNlQuery.Infrastructure.External.Crm;

/// <summary>
/// Holds the CRM connection options such as base URL, API version, domain and credentials.
/// </summary>
public sealed class CrmOptions
{
    public string BaseUrl { get; set; } = "";
    public string ApiVersion { get; set; } = "v9.0";
    public string Domain { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public bool UseIntegratedAuth { get; set; }
    public string MetadataCacheDir { get; set; } = "metadata-cache";
}
