using System.Globalization;

namespace MyWealthV2.Simulator.Bank;

/// <summary>
/// The simulator bank's feed cursor (deposit-matching R44; simulator R25): <c>{WorldId:N}:{place}</c>, the place of
/// the last line returned. A cursor of another world reads the current world from its first place.
/// </summary>
public static class BankFeedCursor
{
    public static string Format(Guid worldId, long place) =>
        $"{worldId:N}:{place.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>The place to read after in <paramref name="worldId"/>, or false when the cursor cannot be read.</summary>
    public static bool TryRead(string? cursor, Guid worldId, out long after)
    {
        after = 0;
        if (cursor is null)
        {
            return true;
        }

        var parts = cursor.Split(':');
        if (parts.Length != 2
            || !Guid.TryParseExact(parts[0], "N", out var cursorWorld)
            || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var place))
        {
            return false;
        }

        after = cursorWorld == worldId ? place : 0;
        return true;
    }
}
