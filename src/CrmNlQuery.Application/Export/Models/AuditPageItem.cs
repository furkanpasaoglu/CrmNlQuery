using CrmNlQuery.Domain.Access.Entities;

namespace CrmNlQuery.Application.Export.Models;

/// <summary>
/// One audit entry returned by a page of the audit pager.
/// </summary>
public sealed record AuditPageItem(long Id, AuditEntry Entry);
