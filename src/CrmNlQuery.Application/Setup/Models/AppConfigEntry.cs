namespace CrmNlQuery.Application.Setup.Models;

/// <summary>
/// One stored setting: its configuration key (for example Crm:BaseUrl), its value as stored (encrypted text when it is a secret) and whether it is a secret.
/// </summary>
public sealed record AppConfigEntry(string Key, string Value, bool Secret);
