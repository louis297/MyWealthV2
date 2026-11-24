using System.Text.Json.Serialization;
using FluentValidation.Results;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.Custodian;
using MyWealthV2.Domain.Entities;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.SafekeepingAccounts;

/// <summary>In-kind transfers R3–R5: a new safekeeping account of the tenant, active.</summary>
[Authorize(Policy = Policies.TenantsManage)]
public class CreateSafekeepingAccountCommand : IRequest<Guid>
{
    [JsonIgnore]
    public Guid RouteTenantId { get; set; }

    public string? Currency { get; init; }

    public string? CustodianName { get; init; }

    public string? AccountName { get; init; }

    public string? AccountNumber { get; init; }

    public string? CustodianConnection { get; init; }

    /// <summary>Refused when present (R4).</summary>
    public Guid? TenantId { get; init; }

    /// <summary>Refused when present (R4).</summary>
    public Guid? Id { get; init; }

    /// <summary>Refused when present (R4).</summary>
    public bool? IsActive { get; init; }
}

public class CreateSafekeepingAccountCommandValidator : AbstractValidator<CreateSafekeepingAccountCommand>
{
    public CreateSafekeepingAccountCommandValidator(ICustodianConnections connections)
    {
        RuleFor(command => command.Currency).NotEmpty();
        RuleFor(command => command.CustodianName)
            .Must(value => TrimmedLength(value) is >= 1 and <= SafekeepingAccount.CustodianNameMaxLength)
            .WithMessage($"Custodian name must be 1 to {SafekeepingAccount.CustodianNameMaxLength} characters.");
        RuleFor(command => command.AccountName)
            .Must(value => TrimmedLength(value) is >= 1 and <= SafekeepingAccount.AccountNameMaxLength)
            .WithMessage($"Account name must be 1 to {SafekeepingAccount.AccountNameMaxLength} characters.");
        RuleFor(command => command.AccountNumber)
            .Must(ClientMoneyAccount.IsValidAccountNumber)
            .WithMessage(
                $"Account number must be 1 to {SafekeepingAccount.AccountNumberMaxLength} letters, digits, spaces and hyphens, with no two spaces in a row.");
        RuleFor(command => command.CustodianConnection)
            .Must(connections.IsConfigured)
            .WithMessage("Unknown custodian connection.");
        RuleFor(command => command.TenantId).Null().WithMessage("TenantId cannot be set.");
        RuleFor(command => command.Id).Null().WithMessage("Id cannot be set.");
        RuleFor(command => command.IsActive).Null().WithMessage("IsActive cannot be set.");
    }

    private static int TrimmedLength(string? value) => value?.Trim().Length ?? 0;
}

public class CreateSafekeepingAccountCommandHandler(IApplicationDbContext db, ICurrencyCatalog catalog)
    : IRequestHandler<CreateSafekeepingAccountCommand, Guid>
{
    public async Task<Guid> Handle(CreateSafekeepingAccountCommand request, CancellationToken cancellationToken)
    {
        var tenant = await SafekeepingAccountLookup.TenantAsync(db, request.RouteTenantId, cancellationToken);
        if (!tenant.IsActive)
        {
            throw new TargetDisabledException("tenant", tenant.PublicId, "Tenant is disabled",
                "Cannot add a safekeeping account while the tenant is disabled. Enable the tenant first.");
        }

        var currency = catalog.TryGet(request.Currency!);
        if (currency is null || !currency.IsActive)
        {
            throw new ValidationException([
                new ValidationFailure(nameof(CreateSafekeepingAccountCommand.Currency), "Currency must be an enabled catalog code.")
            ]);
        }

        var accountNumber = request.AccountNumber!.Trim();
        await SafekeepingAccountLookup.EnsureCanBeActiveAsync(db, tenant.Id, currency.Code, accountNumber, null, cancellationToken);
        var row = SafekeepingAccount.Create(tenant.Id, currency, request.CustodianName!, request.AccountName!, accountNumber,
            request.CustodianConnection!);
        db.SafekeepingAccounts.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        return row.PublicId;
    }
}

/// <summary>R4: disables a safekeeping account. Always allowed.</summary>
[Authorize(Policy = Policies.TenantsManage)]
public class DisableSafekeepingAccountCommand : IRequest
{
    public Guid TenantId { get; set; }

    public Guid Id { get; set; }

    public string? RowVersion { get; init; }
}

public class DisableSafekeepingAccountCommandValidator : AbstractValidator<DisableSafekeepingAccountCommand>
{
    public DisableSafekeepingAccountCommandValidator() => RuleFor(command => command.RowVersion).NotEmpty();
}

public class DisableSafekeepingAccountCommandHandler(IApplicationDbContext db) : IRequestHandler<DisableSafekeepingAccountCommand>
{
    public async Task Handle(DisableSafekeepingAccountCommand request, CancellationToken cancellationToken)
    {
        var row = await SafekeepingAccountLookup.FindAsync(db, request.TenantId, request.Id, cancellationToken);
        if (!string.Equals(Convert.ToBase64String(row.RowVersion ?? []), request.RowVersion, StringComparison.Ordinal))
        {
            throw new ConcurrencyException();
        }

        if (!row.IsActive)
        {
            return;
        }

        row.Disable();
        await db.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>R4, R5: enables a safekeeping account again after the R5 checks.</summary>
[Authorize(Policy = Policies.TenantsManage)]
public class EnableSafekeepingAccountCommand : IRequest
{
    public Guid TenantId { get; set; }

    public Guid Id { get; set; }

    public string? RowVersion { get; init; }
}

public class EnableSafekeepingAccountCommandValidator : AbstractValidator<EnableSafekeepingAccountCommand>
{
    public EnableSafekeepingAccountCommandValidator() => RuleFor(command => command.RowVersion).NotEmpty();
}

public class EnableSafekeepingAccountCommandHandler(IApplicationDbContext db) : IRequestHandler<EnableSafekeepingAccountCommand>
{
    public async Task Handle(EnableSafekeepingAccountCommand request, CancellationToken cancellationToken)
    {
        var row = await SafekeepingAccountLookup.FindAsync(db, request.TenantId, request.Id, cancellationToken);
        if (!string.Equals(Convert.ToBase64String(row.RowVersion ?? []), request.RowVersion, StringComparison.Ordinal))
        {
            throw new ConcurrencyException();
        }

        if (row.IsActive)
        {
            return;
        }

        await SafekeepingAccountLookup.EnsureCanBeActiveAsync(db, row.TenantId, row.Currency, row.AccountNumber, row.Id, cancellationToken);
        row.Enable();
        await db.SaveChangesAsync(cancellationToken);
    }
}
