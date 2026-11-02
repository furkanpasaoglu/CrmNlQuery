using CrmNlQuery.Domain.Access.Entities;
using CrmNlQuery.Domain.Ask.Entities;

namespace CrmNlQuery.Application.Ask.Models;

/// <summary>
/// Request to answer a question: carries the caller identity, answered clarifications, optional previous query, force-continue flag and current form record.
/// </summary>
public sealed record AskRequest(string Question, IReadOnlyDictionary<string, string> Answers, Caller Caller, PreviousQuery? Previous = null, bool ForceContinue = false,
    ResolvedRecord? CurrentRecord = null);
