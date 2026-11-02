using System.Globalization;
using System.Text;

namespace CrmNlQuery.Application.History.Services;

/// <summary>
/// Encodes paging cursors as opaque, URL-safe base64 text carrying position data.
/// </summary>
public static class HistoryCursor
{
    /// <summary>
    /// Encodes the given parts into an opaque cursor string.
    /// </summary>
    public static string Encode(params string[] parts)
    {
        var bytes = Encoding.UTF8.GetBytes(string.Join("|", parts));
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>
    /// Decodes a cursor into the expected number of parts, returning false when it does not match.
    /// </summary>
    public static bool TryDecode(string? cursor, int expected, out string[] parts)
    {
        parts = Array.Empty<string>();
        if (string.IsNullOrWhiteSpace(cursor) || cursor.Length > 200)
        {
            return false;
        }
        try
        {
            var padded = cursor.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
            var text = Encoding.UTF8.GetString(Convert.FromBase64String(padded));
            var split = text.Split('|');
            if (split.Length != expected)
            {
                return false;
            }
            parts = split;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// Builds a (pinned, time, id) cursor for the chat list.
    /// </summary>
    public static string ForConversation(bool pinned, DateTimeOffset updatedAt, string id)
    {
        return Encode(pinned ? "1" : "0", updatedAt.UtcTicks.ToString(CultureInfo.InvariantCulture), id);
    }

    /// <summary>
    /// Reads a chat list cursor into pinned flag, time and id.
    /// </summary>
    public static bool TryReadConversation(string? cursor, out bool pinned, out DateTimeOffset updatedAt, out string id)
    {
        pinned = false;
        updatedAt = default;
        id = "";
        if (!TryDecode(cursor, 3, out var p) || (p[0] != "1" && p[0] != "0") || !TryTicks(p[1], out updatedAt))
        {
            return false;
        }
        pinned = p[0] == "1";
        id = p[2];
        return true;
    }

    /// <summary>
    /// Builds a (time, id) cursor for a chat's messages.
    /// </summary>
    public static string ForMessage(DateTimeOffset createdAt, string id)
    {
        return Encode(createdAt.UtcTicks.ToString(CultureInfo.InvariantCulture), id);
    }

    /// <summary>
    /// Reads a message cursor into time and id.
    /// </summary>
    public static bool TryReadMessage(string? cursor, out DateTimeOffset createdAt, out string id)
    {
        createdAt = default;
        id = "";
        if (!TryDecode(cursor, 2, out var p) || !TryTicks(p[0], out createdAt))
        {
            return false;
        }
        id = p[1];
        return true;
    }

    private static bool TryTicks(string text, out DateTimeOffset value)
    {
        value = default;
        if (!long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
            || ticks < DateTimeOffset.MinValue.UtcTicks || ticks > DateTimeOffset.MaxValue.UtcTicks)
        {
            return false;
        }
        value = new DateTimeOffset(ticks, TimeSpan.Zero);
        return true;
    }
}
