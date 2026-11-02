using System.Text.RegularExpressions;
using CrmNlQuery.Application.Setup.Models;

namespace CrmNlQuery.Application.Setup.Services;

/// <summary>
/// Checks what the wizard collected before it is tested or saved: well-formed http(s) addresses without credentials, sensible lengths, a DOMAIN\user administrator and the confirmations for models outside this machine.
/// </summary>
public static class SetupInputValidator
{
    private static readonly Regex AdminAccountShape = new Regex(@"^[^\\\s]{1,100}\\[^\\\s]{1,100}$", RegexOptions.Compiled);
    public static readonly string[] Providers = { "Ollama", "Vllm", "Anthropic" };

    /// <summary>
    /// Returns null when the CRM connection is valid, otherwise an error code and the field it is about.
    /// </summary>
    public static (string Error, string Field)? ValidateCrm(CrmProbeInput crm)
    {
        if (ParseHttpUrl(crm.BaseUrl) is null) { return ("invalid_url", "crm.baseUrl"); }
        if (!Plain(crm.Domain, 100)) { return ("invalid_value", "crm.domain"); }
        if (!Plain(crm.Username, 100)) { return ("invalid_value", "crm.username"); }
        if (string.IsNullOrEmpty(crm.Password) || crm.Password.Length > 200) { return ("invalid_value", "crm.password"); }
        return null;
    }

    /// <summary>
    /// Returns null when the model settings are valid; a model server outside this machine needs the network confirmation and the cloud model needs the lab confirmation.
    /// </summary>
    public static (string Error, string Field)? ValidateModel(ModelProbeInput model, bool confirmLocalNetwork, bool confirmCloudLab)
    {
        if (!Providers.Contains(model.Provider, StringComparer.Ordinal)) { return ("invalid_value", "model.provider"); }
        if (!Plain(model.Model, 100)) { return ("invalid_value", "model.model"); }
        if (model.ApiKey is not null && (model.ApiKey.Length > 300 || model.ApiKey.Any(char.IsControl))) { return ("invalid_value", "model.apiKey"); }
        if (model.Provider == "Anthropic")
        {
            if (string.IsNullOrWhiteSpace(model.ApiKey)) { return ("invalid_value", "model.apiKey"); }
            return confirmCloudLab ? null : ("confirmation_required", "confirmCloudLab");
        }
        var uri = ParseHttpUrl(model.BaseUrl);
        if (uri is null) { return ("invalid_url", "model.baseUrl"); }
        if (!uri.IsLoopback && !confirmLocalNetwork) { return ("confirmation_required", "confirmLocalNetwork"); }
        return null;
    }

    /// <summary>
    /// Returns null when the whole input is valid, otherwise the first error code and field.
    /// </summary>
    public static (string Error, string Field)? Validate(SetupInput input)
    {
        var first = ValidateCrm(input.Crm) ?? ValidateModel(input.Model, input.ConfirmLocalNetwork, input.ConfirmCloudLab);
        if (first is not null) { return first; }
        if (ParseHttpUrl(input.ServiceUrl) is null) { return ("invalid_url", "serviceUrl"); }
        if (!AdminAccountShape.IsMatch(input.AdminAccount ?? "")) { return ("invalid_value", "adminAccount"); }
        return null;
    }

    /// <summary>
    /// Parses an http or https address without user name, password, query or fragment; returns null for anything else.
    /// </summary>
    public static Uri? ParseHttpUrl(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 500 || text.Any(char.IsControl)) { return null; }
        if (!Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri)) { return null; }
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) { return null; }
        if (string.IsNullOrEmpty(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)) { return null; }
        return uri;
    }

    private static bool Plain(string? value, int max) => !string.IsNullOrWhiteSpace(value) && value.Length <= max && !value.Any(char.IsControl);
}
