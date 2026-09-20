using FluentValidation.Results;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using Microsoft.EntityFrameworkCore;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.Tenants.Commands.UpdateTenant;

[Authorize(Policy = Policies.TenantsManage)]
public class UpdateTenantCommand : IRequest
{
    public Guid Id { get; set; }

    public string? Name { get; init; }

    public string? ReportingCurrency { get; init; }

    public string? RowVersion { get; init; }

    public string? Code { get; init; }
}

public class UpdateTenantCommandValidator : AbstractValidator<UpdateTenantCommand>
{
    public UpdateTenantCommandValidator()
    {
        RuleFor(command => command.Name)
            .Must(name => !string.IsNullOrWhiteSpace(name) && name.Trim().Length is >= 1 and <= 200)
            .WithMessage("Name must be 1 to 200 characters.");
        RuleFor(command => command.RowVersion).NotEmpty();
        RuleFor(command => command.Code)
            .Must(code => code is null)
            .WithMessage("Code cannot be changed.");
    }
}

public class UpdateTenantCommandHandler(IApplicationDbContext db, ICurrencyCatalog catalog)
    : IRequestHandler<UpdateTenantCommand>
{
    public async Task Handle(UpdateTenantCommand request, CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.SingleOrDefaultAsync(row => row.PublicId == request.Id, cancellationToken)
                     ?? throw new NotFoundException("Tenant", request.Id);

        var expected = Convert.ToBase64String(tenant.RowVersion ?? []);
        if (!string.Equals(expected, request.RowVersion, StringComparison.Ordinal))
        {
            throw new ConcurrencyException();
        }

        var name = request.Name!.Trim();
        if (await db.Tenants.AnyAsync(row => row.Id != tenant.Id && row.Name == name, cancellationToken))
        {
            throw new ValidationException([
                new ValidationFailure(nameof(UpdateTenantCommand.Name), "Name must be unique.")
            ]);
        }

        tenant.Rename(name);

        if (request.ReportingCurrency is not null)
        {
            if (string.IsNullOrWhiteSpace(request.ReportingCurrency))
            {
                throw new ValidationException([
                    new ValidationFailure(nameof(UpdateTenantCommand.ReportingCurrency),
                        "Reporting currency must be an enabled catalog code.")
                ]);
            }

            var incoming = request.ReportingCurrency.Trim().ToUpperInvariant();
            if (!string.Equals(incoming, tenant.ReportingCurrency, StringComparison.OrdinalIgnoreCase))
            {
                var currency = catalog.TryGet(incoming);
                if (currency is null || !currency.IsActive)
                {
                    throw new ValidationException([
                        new ValidationFailure(nameof(UpdateTenantCommand.ReportingCurrency),
                            "Reporting currency must be an enabled catalog code.")
                    ]);
                }

                tenant.SetReportingCurrency(currency);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
