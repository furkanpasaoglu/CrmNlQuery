namespace CrmNlQuery.Domain.Create.Enums;

/// <summary>
/// Kinds of writable field types for record creation, described independently of the CRM.
/// </summary>
public enum CreateFieldType { Text, LongText, WholeNumber, Decimal, Boolean, Date, Choice, Lookup }
