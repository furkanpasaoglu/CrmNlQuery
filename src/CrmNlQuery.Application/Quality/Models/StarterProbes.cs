using CrmNlQuery.Domain.Metadata.Entities;

namespace CrmNlQuery.Application.Quality.Models;

/// <summary>
/// Lists which fields need a real query to gather facts: option fields, numeric fields and lookup fields.
/// </summary>
public sealed record StarterProbes(IReadOnlyList<AttributeInfo> Categorical, IReadOnlyList<AttributeInfo> Numeric, IReadOnlyList<(AttributeInfo Attribute, string Target)> Lookups);
