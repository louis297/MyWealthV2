namespace MyWealthV2.Domain.Entities;

/// <summary>
/// Instrument-tax-attributes §5: the FIF treatment of one Instrument for one tax year. Built only by
/// <see cref="Instrument"/>, which passes its own <c>TenantId</c> down. Never <see cref="FifTreatment.NotClassified"/>.
/// </summary>
public class InstrumentFifTreatment : BaseAuditableEntity
{
    private InstrumentFifTreatment()
    {
    }

    public int TenantId { get; private set; }

    public int InstrumentId { get; private set; }

    /// <summary>R8: the calendar year the tax year ends in.</summary>
    public int TaxYear { get; private set; }

    public FifTreatment Treatment { get; private set; }

    public string? Note { get; private set; }

    internal static InstrumentFifTreatment Create(int tenantId, int taxYear, FifTreatment treatment, string? note) =>
        new() { TenantId = tenantId, TaxYear = taxYear, Treatment = treatment, Note = note };

    internal void Set(FifTreatment treatment, string? note)
    {
        Treatment = treatment;
        Note = note;
    }
}
