namespace MyWealthV2.Application.Instruments;

/// <summary>Instrument-tax-attributes R9: the latest tax year a FIF treatment row may be for.</summary>
public static class InstrumentTaxYears
{
    /// <summary>The calendar year of the server's UTC date plus 1. The entity reads no clock (§5).</summary>
    public static int Latest(TimeProvider time) => time.GetUtcNow().UtcDateTime.Year + 1;
}
