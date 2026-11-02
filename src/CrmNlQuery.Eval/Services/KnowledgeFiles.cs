namespace CrmNlQuery.Eval.Services;

/// <summary>
/// Reads the knowledge documents of the eval folder, where each .md file is one document.
/// </summary>
public static class KnowledgeFiles
{
    /// <summary>
    /// Loads documents from several folders given as a semicolon-separated list.
    /// </summary>
    public static IReadOnlyList<(string Title, string Body)> LoadMany(string directories)
    {
        var all = new List<(string Title, string Body)>();
        foreach (var directory in directories.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            all.AddRange(Load(directory));
        }
        return all;
    }

    /// <summary>
    /// Loads the documents of one knowledge folder.
    /// </summary>
    public static IReadOnlyList<(string Title, string Body)> Load(string directory)
    {
        var documents = new List<(string Title, string Body)>();
        foreach (var file in Directory.GetFiles(directory, "*.md").OrderBy(f => f, StringComparer.Ordinal))
        {
            var lines = File.ReadAllText(file).Replace("\r\n", "\n").Split('\n').ToList();
            var hasTitle = lines.Count > 0 && lines[0].StartsWith("# ");
            var title = hasTitle ? lines[0].Substring(2).Trim() : Path.GetFileNameWithoutExtension(file);
            documents.Add((title, string.Join("\n", hasTitle ? lines.Skip(1) : lines).Trim()));
        }
        return documents;
    }
}
