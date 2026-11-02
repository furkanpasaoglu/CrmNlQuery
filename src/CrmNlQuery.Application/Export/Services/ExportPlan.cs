using CrmNlQuery.Application.Export.Models;

namespace CrmNlQuery.Application.Export.Services;

/// <summary>
/// Holds a prepared export: the validated request, file name and the concurrency slot.
/// </summary>
public sealed class ExportPlan : IDisposable
{
    private Action? _release;

    public ExportRequest Request { get; }
    public string FileName { get; }
    public string ContentType { get; }
    public TimeZoneInfo TimeZone { get; }
    public string ZoneName { get; }
    public string? UnavailableZone { get; }

    internal ExportPlan(ExportRequest request, string fileName, string contentType, TimeZoneInfo timeZone, string zoneName, string? unavailableZone, Action release)
    {
        ZoneName = zoneName;
        UnavailableZone = unavailableZone;
        Request = request;
        FileName = fileName;
        ContentType = contentType;
        TimeZone = timeZone;
        _release = release;
    }

    /// <summary>
    /// Releases the concurrent export slot so another export can start.
    /// </summary>
    public void Dispose()
    {
        Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}
