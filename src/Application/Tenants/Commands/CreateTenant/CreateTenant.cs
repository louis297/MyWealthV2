using System.Text.RegularExpressions;
using FluentValidation.Results;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.Tenants.Commands.CreateTenant;

[Authorize(Policy = Policies.TenantsManage)]
public class CreateTenantCommand : IRequest<Guid>
{
    public required string Name { get; init; }

    public required string Code { get; init; }

    public required string ReportingCurrency { get; init; }
}

public class CreateTenantCommandValidator : AbstractValidator<CreateTenantCommand>
{
    private static readonly Regex CodePattern = new("^[a-z0-9-]{2,50}$", RegexOptions.CultureInvariant);

    public CreateTenantCommandValidator()
    {
        RuleFor(command => command.Name)
            .Must(name => !string.IsNullOrWhiteSpace(name) && name.Trim().Length is >= 1 and <= 200)
            .WithMessage("Name must be 1 to 200 characters.");
        RuleFor(command => command.Code)
            .Must(code =>
            {
                var normalised = code?.Trim().ToLowerInvariant();
                return normalised is not null && CodePattern.IsMatch(normalised);
            })
            .WithMessage("Code must be 2 to 50 characters in [a-z0-9-].");
        RuleFor(command => command.ReportingCurrency).NotEmpty();
    }
}

public class CreateTenantCommandHandler(IApplicationDbContext db, ICurrencyCatalog catalog)
    : IRequestHandler<CreateTenantCommand, Guid>
{
    public async Task<Guid> Handle(CreateTenantCommand request, CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        var code = request.Code.Trim().ToLowerInvariant();

        var currency = catalog.TryGet(request.ReportingCurrency);
        if (currency is null || !currency.IsEnabled)
        {
            throw new ValidationException([
                new ValidationFailure(nameof(CreateTenantCommand.ReportingCurrency),
                    "Reporting currency must be an enabled catalog code.")
            ]);
        }

        if (await db.Tenants.AnyAsync(tenant => tenant.Name == name, cancellationToken))
        {
            throw new ValidationException([
                new ValidationFailure(nameof(CreateTenantCommand.Name), "Name must be unique.")
            ]);
        }

        if (await db.Tenants.AnyAsync(tenant => tenant.Code == code, cancellationToken))
        {
            throw new ValidationException([
                new ValidationFailure(nameof(CreateTenantCommand.Code), "Code must be unique.")
            ]);
        }

        var tenant = Tenant.Create(name, code, currency);
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync(cancellationToken);
        return tenant.PublicId;
    }
}
