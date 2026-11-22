using System.Globalization;

namespace MyWealthV2.BackOffice.Components.Pages;

/// <summary>Corporate actions BO25: the labels of the announcements table.</summary>
public static class AnnouncementText
{
    public static readonly string[] Statuses = ["Received", "Processed", "Skipped"];

    public static string Reason(string? reason) => reason switch
    {
        null => "",
        "UnsupportedKind" => "Kind not supported",
        "Invalid" => "Not valid",
        "NoInstrument" => "No firm lists this instrument",
        "AlreadyEffective" => "Already applied",
        "Superseded" => "An older version",
        _ => reason
    };

    public static string Kind(string? kind) => kind ?? "Unknown";

    public static string Ratio(CorporateActionAnnouncementItem item) =>
        string.Create(CultureInfo.InvariantCulture, $"{item.RatioNew} for {item.RatioHeld}");

    public static string Instant(DateTimeOffset at) =>
        at.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);

    /// <summary>BO25: an announcement skipped as AlreadyEffective needs a person.</summary>
    public static bool NeedsPerson(CorporateActionAnnouncementItem item) => item.SkipReason == "AlreadyEffective";
}
