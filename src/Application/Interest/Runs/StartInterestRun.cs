using System.Text.Json.Serialization;
using FluentValidation.Results;
using Microsoft.Extensions.Options;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Enums;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.Interest.Runs;

/// <summary>Interest-run R32: a SystemAdmin starts a run for a Tenant and an as-of date; it runs at once.</summary>
[Authorize(Policy = Policies.InterestRunsStart)]
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public class StartInterestRunCommand : IRequest<Guid>
{
    public Guid? TenantId { get; init; }

    public DateOnly? AsOfDate { get; init; }
}

public class StartInterestRunCommandValidator : AbstractValidator<StartInterestRunCommand>
{
    public StartInterestRunCommandValidator()
    {
        RuleFor(command => command.TenantId).NotEmpty().WithMessage("TenantId is required.");
        RuleFor(command => command.AsOfDate).NotNull().WithMessage("AsOfDate is required.");
    }
}

public class StartInterestRunCommandHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    IRunInterest runInterest,
    IOptions<InterestOptions> options,
    TimeProvider time)
    : IRequestHandler<StartInterestRunCommand, Guid>
{
    public const string FutureMessage = "Interest can be run only for days that have ended.";

    public async Task<Guid> Handle(StartInterestRunCommand request, CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.IgnoreQueryFilters().AsNoTracking()
                         .SingleOrDefaultAsync(row => row.PublicId == request.TenantId, cancellationToken)
                     ?? throw new NotFoundException("Tenant", request.TenantId!);
        var asOfDate = request.AsOfDate!.Value;
        var yesterday = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime).AddDays(-1);
        if (asOfDate > yesterday && !options.Value.AllowFutureAsOfDates)
        {
            throw new ValidationException([new ValidationFailure(nameof(StartInterestRunCommand.AsOfDate), FutureMessage)]);
        }

        var person = currentUser.Person?.Id;
        var run = await runInterest.RunAsync(tenant.Id, asOfDate, InterestRunTrigger.Manual, person, cancellationToken);
        return run.PublicId;
    }
}
