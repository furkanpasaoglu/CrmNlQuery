using System.Text.Json;
using CrmNlQuery.Application.Create.Abstractions;
using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Domain.Create.Entities;
using CrmNlQuery.Application.Common.Exceptions;

namespace CrmNlQuery.Infrastructure.External.Crm.Records;

/// <summary>
/// Writes a new record to the CRM always as the calling user, so CRM applies that user's create permission.
/// </summary>
public sealed class CrmRecordWriter : IRecordWriter
{
    private readonly CrmClient _client;

    public CrmRecordWriter(CrmClient client)
    {
        _client = client;
    }

    /// <summary>
    /// Creates a record in the CRM as the caller from the validated field values and returns the outcome.
    /// </summary>
    public async Task<Guid> CreateAsync(CreateSchema schema, IReadOnlyDictionary<string, object?> payload, Caller caller, CancellationToken ct = default)
    {
        var body = JsonSerializer.Serialize(payload);
        using var created = await _client.SendJsonAsync(HttpMethod.Post, schema.EntitySet, body, caller.UserId, ct);
        if (created is not null
            && created.RootElement.TryGetProperty(schema.PrimaryIdAttribute, out var id)
            && id.ValueKind == JsonValueKind.String
            && Guid.TryParse(id.GetString(), out var guid))
        {
            return guid;
        }
        throw new CrmException("CRM created the record but did not return its id.");
    }
}
