using System.Text.Json.Serialization;
using FluentValidation.Results;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Exceptions;
using MyWealthV2.Domain.Instruments;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

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

public class SetInstrumentTaxAttributesCommandValidator : AbstractValidator<SetInstrumentTaxAttributesCommand>
{
    /// <summary>R9: the five values a row may hold, by exact name.</summary>
    private static readonly string[] StoredTreatments =
        Enum.GetValues<FifTreatment>().Where(treatment => treatment.IsStored()).Select(treatment => treatment.ToString()).ToArray();

    public SetInstrumentTaxAttributesCommandValidator(TimeProvider time)
    {
        RuleFor(command => command.RowVersion).NotEmpty();
        RuleFor(command => command.IssuerCountry)
            .Must((command, _) => command.IssuerCountrySent)
            .WithMessage("IssuerCountry is required; send null for no country.");
        RuleFor(command => command.FifTreatments)
            .NotNull()
            .WithMessage("FifTreatments is required; send an empty list for none.");
        RuleFor(command => command)
            .Custom((command, context) =>
            {
                foreach (var refusal in Refusals(command, InstrumentTaxYears.Latest(time)))
                {
                    context.AddFailure(refusal.Field, refusal.Message);
                }
            })
            .When(command => command.IssuerCountrySent && command.FifTreatments is not null);
    }

    /// <summary>
    /// The shape of each row, then the rules of <see cref="Instrument.TaxAttributeRefusals"/> (R5, R9, R12). A body
    /// with a row of the wrong shape is checked for its country only, so no index points at the wrong row.
    /// </summary>
    private static IEnumerable<TaxAttributeRefusal> Refusals(SetInstrumentTaxAttributesCommand command, int latestTaxYear)
    {
        var shapes = new List<TaxAttributeRefusal>();
        var rows = new List<FifTreatmentInput>();
        for (var index = 0; index < command.FifTreatments!.Count; index++)
        {
            var prefix = $"{nameof(SetInstrumentTaxAttributesCommand.FifTreatments)}[{index}]";
            if (command.FifTreatments[index] is not { } row)
            {
                shapes.Add(new(prefix, "A FIF treatment row is required."));
                continue;
            }

            if (row.TaxYear is null)
            {
                shapes.Add(new($"{prefix}.{nameof(FifTreatmentRow.TaxYear)}", "TaxYear is required."));
            }

            if (row.Treatment is null || !StoredTreatments.Contains(row.Treatment, StringComparer.Ordinal))
            {
                shapes.Add(new($"{prefix}.{nameof(FifTreatmentRow.Treatment)}",
                    "Treatment must be NotForeign, ExemptAsxListed, ExemptOther, Attributing, or AttributingNoFdr."));
            }

            if (row.TaxYear is { } taxYear && row.Treatment is not null && StoredTreatments.Contains(row.Treatment, StringComparer.Ordinal))
            {
                rows.Add(new FifTreatmentInput(taxYear, Enum.Parse<FifTreatment>(row.Treatment), row.Note));
            }
        }

        return shapes.Count == 0
            ? Instrument.TaxAttributeRefusals(command.IssuerCountry, rows, latestTaxYear)
            : shapes.Concat(Instrument.TaxAttributeRefusals(command.IssuerCountry, [], latestTaxYear));
    }
}

public class SetInstrumentTaxAttributesCommandHandler(IApplicationDbContext db, ICurrentUser currentUser, TimeProvider timeProvider)
    : IRequestHandler<SetInstrumentTaxAttributesCommand>
{
    public async Task Handle(SetInstrumentTaxAttributesCommand request, CancellationToken cancellationToken)
    {
        var query = db.Instruments.Include(row => row.FifTreatments).Where(row => row.PublicId == request.Id);
        if (currentUser.Role != UserRole.SystemAdmin)
        {
            query = query.Where(row => row.TenantId == currentUser.TenantId);
        }

        var instrument = await query.SingleOrDefaultAsync(cancellationToken)
                         ?? throw new NotFoundException("Instrument", request.Id);

        if (!string.Equals(Convert.ToBase64String(instrument.RowVersion ?? []), request.RowVersion, StringComparison.Ordinal))
        {
            throw new ConcurrencyException();
        }

        var rows = request.FifTreatments!
            .Select(row => new FifTreatmentInput(row!.TaxYear!.Value, Enum.Parse<FifTreatment>(row.Treatment!), row.Note))
            .ToList();
        var before = instrument.FifTreatments.ToList();
        try
        {
            instrument.SetTaxAttributes(request.IssuerCountry, rows, InstrumentTaxYears.Latest(timeProvider));
        }
        catch (DomainException exception)
        {
            throw new ValidationException([
                new ValidationFailure(nameof(SetInstrumentTaxAttributesCommand.FifTreatments), exception.Message)
            ]);
        }

        db.InstrumentFifTreatments.RemoveRange(before.Except(instrument.FifTreatments));

        // R17: the Instrument row is always written, so its rowVersion moves even when only a row changed.
        db.MarkModified(instrument);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyException();
        }
    }
}
