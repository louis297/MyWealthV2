using System.Text.Json.Serialization;
using MyWealthV2.Application.Common.Security;

namespace MyWealthV2.Application.Instruments.Commands.SetInstrumentTaxAttributes;

/// <summary>
/// Instrument-tax-attributes R16: <c>PUT /instruments/{id}/tax-attributes</c> replaces the issuer country and every
/// FIF treatment row. Both are required: <c>issuerCountry</c> may be <c>null</c>, <c>fifTreatments</c> may be empty.
/// </summary>
[Authorize(Policy = Policies.InstrumentsManage)]
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public class SetInstrumentTaxAttributesCommand : IRequest
{
    private string? _issuerCountry;

    [JsonIgnore]
    public Guid Id { get; set; }

    public string? IssuerCountry
    {
        get => _issuerCountry;
        init
        {
            _issuerCountry = value;
            IssuerCountrySent = true;
        }
    }

    /// <summary>True when the body carried <c>issuerCountry</c>, a code or <c>null</c>.</summary>
    [JsonIgnore]
    public bool IssuerCountrySent { get; private init; }

    public List<FifTreatmentRow?>? FifTreatments { get; init; }

    public string? RowVersion { get; init; }
}

/// <summary>One row of the body: <c>{ taxYear, treatment, note }</c>.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public class FifTreatmentRow
{
    public int? TaxYear { get; init; }

    public string? Treatment { get; init; }

    public string? Note { get; init; }
}

public class SetInstrumentTaxAttributesCommandHandler
    : IRequestHandler<SetInstrumentTaxAttributesCommand>
{
    public Task Handle(SetInstrumentTaxAttributesCommand request, CancellationToken cancellationToken) => Task.CompletedTask;
}
