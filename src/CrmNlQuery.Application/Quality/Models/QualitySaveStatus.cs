namespace CrmNlQuery.Application.Quality.Models;

/// <summary>
/// Type used by the quality-check feature for save status.
/// </summary>
public enum QualitySaveStatus { Saved, Duplicate, NotFound, Invalid, TooWeak, NotSupported, DataClosed, LimitReached, TooManySuggestions, Disabled }
