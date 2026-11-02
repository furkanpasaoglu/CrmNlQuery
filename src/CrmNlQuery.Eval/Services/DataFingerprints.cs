using System.Text.Json;

namespace CrmNlQuery.Eval.Services;

/// <summary>
/// Manages stored reference fingerprints per case so changes in the data can be flagged.
/// </summary>
public static class DataFingerprints
{
    public const string DefaultPath = "eval/data-fingerprints.json";

    /// <summary>
    /// Loads the fingerprint file into a dictionary.
    /// </summary>
    public static Dictionary<string, string> Load(string path)
    {
        if (!File.Exists(path)) { return new Dictionary<string, string>(); }
        try { return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? new Dictionary<string, string>(); }
        catch (JsonException) { return new Dictionary<string, string>(); }
    }

    /// <summary>
    /// Saves fingerprints, re-reading the file first so parallel packs do not overwrite each other.
    /// </summary>
    public static void Save(string path, Dictionary<string, string> values)
    {
        var merged = Load(path);
        foreach (var (key, value) in values) { merged[key] = value; }
        values = merged;
        var ordered = values.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToDictionary(kv => kv.Key, kv => kv.Value);
        File.WriteAllText(path, JsonSerializer.Serialize(ordered, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>
    /// Extracts the row count from a fingerprint such as "4:abc".
    /// </summary>
    public static int? RowsOf(string fingerprint)
        => int.TryParse(fingerprint.Split(':')[0], out var n) ? n : null;
}
