namespace CrmNlQuery.Tests.Support;

/// <summary>
/// Marks a test that needs Windows (for example SID name translation); it is skipped on other operating systems.
/// </summary>
public sealed class WindowsOnlyFactAttribute : FactAttribute
{
    public WindowsOnlyFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "Needs Windows.";
        }
    }
}
