using CrmNlQuery.Application.Access.Models;

namespace CrmNlQuery.Application.Access.Abstractions;

/// <summary>
/// Reads the CRM record a browser created for a challenge, with the service account.
/// </summary>
public interface ICrmProofReader
{
    /// <summary>
    /// Finds the proof record for the nonce and returns who created it, or null when no record or more than one record matches the subject.
    /// </summary>
    Task<CrmProof?> ReadAsync(string nonce, CancellationToken ct = default);
}
