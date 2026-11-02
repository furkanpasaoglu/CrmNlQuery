using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Application.Common.Exceptions;

namespace CrmNlQuery.Infrastructure.External.Crm;

/// <summary>
/// Sends HTTP requests to the Dynamics CRM Web API, with every read and write tied to a caller identity.
/// </summary>
public sealed class CrmClient
{
    private readonly HttpClient _http;
    private readonly string _apiRoot;
    private readonly bool _allowEvaluationCaller;

    public CrmClient(HttpClient http, CrmOptions options, bool allowEvaluationCaller = false)
    {
        _http = http;
        _allowEvaluationCaller = allowEvaluationCaller;
        _apiRoot = options.BaseUrl.TrimEnd('/') + "/api/data/" + options.ApiVersion + "/";
    }

    /// <summary>
    /// Creates the HTTP handler configured for the CRM options such as credentials.
    /// </summary>
    public static HttpClientHandler CreateHandler(CrmOptions options)
    {
        return options.UseIntegratedAuth
            ? new HttpClientHandler { UseDefaultCredentials = true }
            : new HttpClientHandler { Credentials = new NetworkCredential(options.Username, options.Password, options.Domain) };
    }

    /// <summary>
    /// Creates a CRM client from options, optionally allowing the evaluation caller.
    /// </summary>
    public static CrmClient Create(CrmOptions options, bool allowEvaluationCaller = false)
    {
        var http = new HttpClient(CreateHandler(options)) { Timeout = TimeSpan.FromSeconds(120) };
        return new CrmClient(http, options, allowEvaluationCaller);
    }

    /// <summary>
    /// Reads from the CRM as the calling user via impersonation so the CRM applies its own security roles; an empty identity never falls back to the service account.
    /// </summary>
    public async Task<JsonDocument> GetAsync(string relativeUrl, Caller caller, CancellationToken ct = default)
    {
        if (caller.IsEvaluation && !_allowEvaluationCaller)
        {
            throw new InvalidOperationException("The evaluation caller (service-account data queries) is not allowed in this configuration.");
        }
        return await ReadAsync(relativeUrl, caller.IsEvaluation ? null : caller.UserId, ct);
    }

    /// <summary>
    /// Reads system data such as schema and view columns with the service account, never row data and only through the service read policy.
    /// </summary>
    public Task<JsonDocument> GetAsServiceAsync(string relativeUrl, CancellationToken ct = default)
    {
        ServiceReadPolicy.EnsureAllowed(relativeUrl);
        return ReadAsync(relativeUrl, null, ct);
    }

    private async Task<JsonDocument> ReadAsync(string relativeUrl, Guid? impersonate, CancellationToken ct)
    {
        using var request = NewRequest(HttpMethod.Get, relativeUrl, impersonate);
        request.Headers.Add("Prefer", "odata.include-annotations=\"*\"");
        var body = await SendAsync(request, ct);
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            throw new CrmException("CRM returned a response that is not JSON (is the sign-in still valid?).", null, ex);
        }
    }

    /// <summary>
    /// Sends a POST, PATCH or DELETE as the given caller, currently used only for Open in CRM personal views.
    /// </summary>
    public async Task<JsonDocument?> SendJsonAsync(HttpMethod method, string relativeUrl, string? json, Guid callerId, CancellationToken ct = default)
    {
        if (method == HttpMethod.Get)
        {
            throw new ArgumentException("Use GetAsync for reads.", nameof(method));
        }
        using var request = NewRequest(method, relativeUrl, callerId);
        if (json is not null)
        {
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }
        if (method == HttpMethod.Post)
        {
            request.Headers.Add("Prefer", "return=representation");
        }
        var body = await SendAsync(request, ct);
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            throw new CrmException("CRM returned a response that is not JSON (is the sign-in still valid?).", null, ex);
        }
    }

    private HttpRequestMessage NewRequest(HttpMethod method, string relativeUrl, Guid? callerId)
    {
        var request = new HttpRequestMessage(method, _apiRoot + relativeUrl);
        request.Headers.Add("Accept", "application/json");
        request.Headers.Add("OData-MaxVersion", "4.0");
        request.Headers.Add("OData-Version", "4.0");
        if (callerId is not null)
        {
            request.Headers.Add("MSCRMCallerID", callerId.Value.ToString());
        }
        return request;
    }

    private async Task<string> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new CrmException("Cannot reach CRM: " + ex.Message, null, ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new CrmException("CRM did not answer in time.", null, ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                if (body.Contains("0x80040204", StringComparison.OrdinalIgnoreCase)
                    || body.Contains("act on behalf", StringComparison.OrdinalIgnoreCase))
                {
                    throw new CrmException(
                        CrmNlQuery.Application.Localization.Services.MessageCatalog.Now(CrmNlQuery.Application.Localization.Models.Msg.CrmActOnBehalf),
                        (int)response.StatusCode, null, false, true);
                }
                if (IsMissingPrivilege(body))
                {
                    throw new CrmException(PermissionMessage(body), (int)response.StatusCode, null, true);
                }
                var shortBody = body.Length > 300 ? body.Substring(0, 300) : body;
                throw new CrmException($"CRM returned {(int)response.StatusCode}: {shortBody}", (int)response.StatusCode, null, false, false, IsAggregateLimit(body));
            }
            return body;
        }
    }

    private static bool IsAggregateLimit(string body)
    {
        return body.Contains("0x8004E023", StringComparison.OrdinalIgnoreCase)
            || body.Contains("AggregateQueryRecordLimit", StringComparison.OrdinalIgnoreCase)
            || body.Contains("maximum record limit", StringComparison.OrdinalIgnoreCase);
    }

    private static readonly Regex ReadPrivilege = new Regex(@"missing prvRead(?<entity>[A-Za-z0-9_]+) privilege", RegexOptions.IgnoreCase);

    private static bool IsMissingPrivilege(string body)
    {
        return body.Contains("0x80040220", StringComparison.OrdinalIgnoreCase)
            || Regex.IsMatch(body, @"missing prv[A-Za-z0-9_]+ privilege", RegexOptions.IgnoreCase);
    }

    private static string PermissionMessage(string body)
    {
        var match = ReadPrivilege.Match(body);
        if (match.Success)
        {
            return CrmNlQuery.Application.Localization.Services.MessageCatalog.Now(CrmNlQuery.Application.Localization.Models.Msg.CrmReadEntity, CrmNlQuery.Application.Localization.Services.MessageCatalog.Args(("entity", match.Groups["entity"].Value)));
        }
        return Regex.IsMatch(body, @"missing prv(Create|Write|Delete)[A-Za-z0-9_]+ privilege", RegexOptions.IgnoreCase)
            ? CrmNlQuery.Application.Localization.Services.MessageCatalog.Now(CrmNlQuery.Application.Localization.Models.Msg.CrmChangeKind)
            : CrmNlQuery.Application.Localization.Services.MessageCatalog.Now(CrmNlQuery.Application.Localization.Models.Msg.CrmReadKind);
    }
}
