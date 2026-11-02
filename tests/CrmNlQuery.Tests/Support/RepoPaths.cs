using System.Runtime.CompilerServices;

namespace CrmNlQuery.Tests.Support;

/// <summary>
/// Provides the repository root for tests that read the source tree.
/// </summary>
public static class RepoPaths
{
    /// <summary>
    /// Returns the repository root path, found by walking up from this file.
    /// </summary>
    public static string Root() => FindFrom();

    private static string FindFrom([CallerFilePath] string thisFile = "")
    {
        var dir = Path.GetDirectoryName(thisFile);
        while (dir is not null && !File.Exists(Path.Combine(dir, "CrmNlQuery.slnx")))
        {
            dir = Path.GetDirectoryName(dir);
        }
        return dir ?? throw new InvalidOperationException("Depo kökü (CrmNlQuery.slnx) bulunamadı.");
    }
}
