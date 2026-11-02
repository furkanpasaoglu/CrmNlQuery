namespace CrmNlQuery.Application.Quality.Runs;

/// <summary>
/// Lists the outcomes of one scheduler tick, such as started, skipped or waiting.
/// </summary>
public enum ScheduleTick
{
    Disabled,
    NotDue,
    AlreadyToday,
    NoOwner,
    CannotResolve,
    Busy,
    Empty,
    Started,
}
