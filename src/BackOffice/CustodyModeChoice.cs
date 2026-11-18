namespace MyWealthV2.BackOffice;

/// <summary>
/// The two custody mode boxes on the tenant pages (back-office BO14, BO15). The form may be sent only while
/// at least one box is checked; it sends exactly the checked values as <c>allowedCustodyModes</c>.
/// </summary>
public sealed class CustodyModeChoice
{
    public const string External = "External";
    public const string Custodial = "Custodial";

    public bool AllowsExternal { get; set; }

    public bool AllowsCustodial { get; set; }

    public bool CanSubmit => AllowsExternal || AllowsCustodial;

    public static CustodyModeChoice Both() => new() { AllowsExternal = true, AllowsCustodial = true };

    public static CustodyModeChoice From(IReadOnlyList<string> modes) => new()
    {
        AllowsExternal = modes.Contains(External),
        AllowsCustodial = modes.Contains(Custodial)
    };

    public IReadOnlyList<string> ToList()
    {
        var modes = new List<string>(2);
        if (AllowsExternal)
        {
            modes.Add(External);
        }

        if (AllowsCustodial)
        {
            modes.Add(Custodial);
        }

        return modes;
    }
}
