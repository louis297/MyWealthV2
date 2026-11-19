namespace MyWealthV2.BackOffice;

/// <summary>Reads the <c>errors</c> of a 400 validation problem from the platform API, so a page can show each at its field.</summary>
public static class ValidationErrors
{
    /// <summary>
    /// The first message per field of a 400 with <c>errors</c>, keyed by field name without regard to case.
    /// Empty for any other status or body.
    /// </summary>
    public static IReadOnlyDictionary<string, string> From(PlatformApiException exception) =>
        throw new NotImplementedException();
}
