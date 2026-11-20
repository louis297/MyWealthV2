using System.Text.Json.Serialization;
using FluentValidation.Results;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Entities;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.ClientMoneyAccounts.Commands.CreateClientMoneyAccount;

/// <summary>Client money accounts R2, R4–R6 (docs/features/client-money-accounts.md §4.1); the bank connection,
/// deposit-matching R2.</summary>
[Authorize(Policy = Policies.TenantsManage)]
public class CreateClientMoneyAccountCommand : IRequest<Guid>
{
    /// <summary>The tenant from the route. Not read from the body.</summary>
    [JsonIgnore]
    public Guid RouteTenantId { get; set; }

    public string? Currency { get; init; }

    public string? BankName { get; init; }

    public string? AccountName { get; init; }

    public string? AccountNumber { get; init; }

    /// <summary>A bank connection configured on this host (deposit-matching R2).</summary>
    public string? BankConnection { get; init; }

    /// <summary>Refused when present (R6).</summary>
    public Guid? TenantId { get; init; }

    /// <summary>Refused when present (R6).</summary>
    public Guid? Id { get; init; }

    /// <summary>Refused when present (R6).</summary>
    public bool? IsActive { get; init; }
}

public class CreateClientMoneyAccountCommandValidator : AbstractValidator<CreateClientMoneyAccountCommand>
{
    public CreateClientMoneyAccountCommandValidator()
    {
        RuleFor(command => command.Currency).NotEmpty();
        RuleFor(command => command.BankName)
            .Must(value => TrimmedLength(value) is >= 1 and <= ClientMoneyAccount.BankNameMaxLength)
            .WithMessage($"Bank name must be 1 to {ClientMoneyAccount.BankNameMaxLength} characters.");
        RuleFor(command => command.AccountName)
            .Must(value => TrimmedLength(value) is >= 1 and <= ClientMoneyAccount.AccountNameMaxLength)
            .WithMessage($"Account name must be 1 to {ClientMoneyAccount.AccountNameMaxLength} characters.");
        RuleFor(command => command.AccountNumber)
            .Must(ClientMoneyAccount.IsValidAccountNumber)
            .WithMessage(
                $"Account number must be 1 to {ClientMoneyAccount.AccountNumberMaxLength} letters, digits, spaces and hyphens, with no two spaces in a row.");
        RuleFor(command => command.TenantId).Null().WithMessage("TenantId cannot be set.");
        RuleFor(command => command.Id).Null().WithMessage("Id cannot be set.");
        RuleFor(command => command.IsActive).Null().WithMessage("IsActive cannot be set.");
    }

    private static int TrimmedLength(string? value) => value?.Trim().Length ?? 0;
}

public class CreateClientMoneyAccountCommandHandler(IApplicationDbContext db, ICurrencyCatalog catalog)
    : IRequestHandler<CreateClientMoneyAccountCommand, Guid>
{
    public async Task<Guid> Handle(CreateClientMoneyAccountCommand request, CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.AsNoTracking()
                         .SingleOrDefaultAsync(row => row.PublicId == request.RouteTenantId, cancellationToken)
                     ?? throw new NotFoundException("Tenant", request.RouteTenantId);

        if (!tenant.IsActive)
        {
            throw new TargetDisabledException(
                "tenant",
                tenant.PublicId,
                "Tenant is disabled",
                "Cannot add a client money account while the tenant is disabled. Enable the tenant first.");
        }

        var currency = catalog.TryGet(request.Currency!);
        if (currency is null || !currency.IsActive)
        {
            throw new ValidationException([
                new ValidationFailure(nameof(CreateClientMoneyAccountCommand.Currency),
                    "Currency must be an enabled catalog code.")
            ]);
        }

        var accountNumber = request.AccountNumber!.Trim();
        await ClientMoneyAccountRules.EnsureCanBeActiveAsync(
            db, tenant.Id, currency.Code, accountNumber, exceptId: null, cancellationToken);

        var row = ClientMoneyAccount.Create(
            tenant.Id, currency, request.BankName!, request.AccountName!, accountNumber, request.BankConnection ?? string.Empty);
        db.ClientMoneyAccounts.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        return row.PublicId;
    }
}
