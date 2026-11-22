using System.Text.Json;
using System.Text.Json.Serialization;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.Interest.TaxProfiles;

/// <summary>Interest-run R3, R6, R7: <c>PUT /users/customers/{customerId}/tax-profile</c>. All three attributes are
/// required; <c>rowVersion</c> is the one the read returned, <c>null</c> while no profile is saved.</summary>
[Authorize(Policy = Policies.CustomersManage + "," + Policies.CustomersManageOwn)]
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public class SetCustomerTaxProfileCommand : IRequest
{
    private decimal? _rwtRatePercent;

    [JsonIgnore]
    public Guid Path { get; set; }

    public string? TaxResidency { get; init; }

    public bool? IrdNumberHeld { get; init; }

    public decimal? RwtRatePercent
    {
        get => _rwtRatePercent;
        init
        {
            _rwtRatePercent = value;
            RwtRatePercentSent = true;
        }
    }

    /// <summary>True when the body carried <c>rwtRatePercent</c>, a number or <c>null</c>.</summary>
    [JsonIgnore]
    public bool RwtRatePercentSent { get; private init; }

    public string? RowVersion { get; init; }

    public JsonElement? TenantId { get; init; }

    public JsonElement? CustomerId { get; init; }

    public JsonElement? Withholding { get; init; }
}

public class SetCustomerTaxProfileCommandValidator : AbstractValidator<SetCustomerTaxProfileCommand>
{
    public SetCustomerTaxProfileCommandValidator()
    {
        RuleFor(command => command.TaxResidency)
            .Must(residency => residency is nameof(TaxResidency.NewZealand) or nameof(TaxResidency.Overseas))
            .WithMessage("Tax residency must be NewZealand or Overseas.");
        RuleFor(command => command.IrdNumberHeld)
            .NotNull()
            .WithMessage("IrdNumberHeld is required.");
        RuleFor(command => command.RwtRatePercent)
            .Must((command, _) => command.RwtRatePercentSent)
            .WithMessage("RwtRatePercent is required; send null for no rate.");
        RuleFor(command => command.RwtRatePercent)
            .Must((command, rate) => CustomerTaxProfile.RefusalOf(
                Enum.Parse<TaxResidency>(command.TaxResidency!), command.IrdNumberHeld!.Value, rate) is null)
            .WithMessage((command, rate) => CustomerTaxProfile.RefusalOf(
                Enum.Parse<TaxResidency>(command.TaxResidency!), command.IrdNumberHeld!.Value, rate)!)
            .When(command => command.RwtRatePercentSent
                             && command.TaxResidency is nameof(TaxResidency.NewZealand) or nameof(TaxResidency.Overseas)
                             && command.IrdNumberHeld is not null);
        RuleFor(command => command.TenantId)
            .Must(value => value is null)
            .WithMessage("TenantId cannot be set; the tenant is the caller's.");
        RuleFor(command => command.CustomerId)
            .Must(value => value is null)
            .WithMessage("CustomerId cannot be set; the customer is the path's.");
        RuleFor(command => command.Withholding)
            .Must(value => value is null)
            .WithMessage("Withholding is worked out from the profile and cannot be set.");
    }
}

public class SetCustomerTaxProfileCommandHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    TimeProvider timeProvider)
    : IRequestHandler<SetCustomerTaxProfileCommand>
{
    public async Task Handle(SetCustomerTaxProfileCommand request, CancellationToken cancellationToken)
    {
        var customer = await TaxProfileReader.CustomerAsync(db, currentUser, request.Path, cancellationToken);
        var residency = Enum.Parse<TaxResidency>(request.TaxResidency!);
        var irdNumberHeld = request.IrdNumberHeld!.Value;
        var by = currentUser.Person!.Id;
        var at = timeProvider.GetUtcNow();

        var profile = await db.CustomerTaxProfiles.SingleOrDefaultAsync(row => row.UserId == customer.Id, cancellationToken);
        if (profile is null)
        {
            if (request.RowVersion is not null)
            {
                throw new ConcurrencyException();
            }

            db.CustomerTaxProfiles.Add(CustomerTaxProfile.Create(
                customer.TenantId!.Value, customer.Id, residency, irdNumberHeld, request.RwtRatePercent, by, at));
        }
        else
        {
            if (!string.Equals(Convert.ToBase64String(profile.RowVersion ?? []), request.RowVersion, StringComparison.Ordinal))
            {
                throw new ConcurrencyException();
            }

            profile.Set(residency, irdNumberHeld, request.RwtRatePercent, by, at);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyException();
        }
        catch (Common.Exceptions.ValidationException) when (profile is null)
        {
            // Another first write won the race on the key (R6).
            throw new ConcurrencyException();
        }
    }
}
