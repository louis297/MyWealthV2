namespace MyWealthV2.Application.Reports;

public static partial class CustomerFifReport
{
    private sealed partial class Report
    {
        /// <summary>Sections 6 to 9 (R21 to R24).</summary>
        private IEnumerable<CustomerFifSection> LaterSections() =>
        [
            new("6. Each holding", []),
            new("7. Tax taken from dividends", []),
            new("8. Prices, rates and values used", []),
            new("9. What to know about these figures", [])
        ];
    }
}
