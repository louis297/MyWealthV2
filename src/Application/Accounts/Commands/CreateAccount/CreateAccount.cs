using FluentValidation.Results;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.Accounts.Commands.CreateAccount;

[Authorize(Policy = Policies.AccountsCreate)]
public class CreateAccountCommand : IRequest<Guid>
{
    public Guid? TenantId { get; init; }

    public Guid CustomerId { get; init; }

    public required string Name { get; init; }

    public required string Type { get; init; }

    public string? CustodyMode { get; init; }

    public required string Currency { get; init; }

    public string? Status { get; init; }

    public bool? IsActive { get; init; }
}

public class CreateAccountCommandValidator : AbstractValidator<CreateAccountCommand>
{
    private static readonly string[] Phase2Types = ["Bank", "Cash", "Brokerage", "Other"];

    public CreateAccountCommandValidator(ICurrentUser currentUser)
    {
        RuleFor(command => command.CustomerId).NotEmpty();
        RuleFor(command => command.Name)
            .Must(name => !string.IsNullOrWhiteSpace(name) && name.Trim().Length is >= 1 and <= 200)
            .WithMessage("Name must be 1 to 200 characters.");
        RuleFor(command => command.Type)
            .Must(type => type is not null && Phase2Types.Contains(type))
            .WithMessage("Type must be Bank, Cash, Brokerage, or Other.");
        RuleFor(command => command.CustodyMode)
            .Must(custodyMode => custodyMode is nameof(CustodyMode.External) or nameof(CustodyMode.Custodial))
            .WithMessage("CustodyMode must be External or Custodial.");
        RuleFor(command => command.Type)
            .Must(type => type == nameof(AccountType.Brokerage))
            .When(command => command.CustodyMode == nameof(CustodyMode.Custodial)
                             && command.Type is not null && Phase2Types.Contains(command.Type))
            .WithMessage(Account.CustodialTypeMessage);
        RuleFor(command => command.Currency).NotEmpty();
        RuleFor(command => command.Status)
            .Must(status => status is null)
            .WithMessage("Status cannot be set.");
        RuleFor(command => command.IsActive)
            .Must(isActive => isActive is null)
            .WithMessage("IsActive cannot be set.");

        When(_ => currentUser.Role == UserRole.SystemAdmin, () =>
        {
            RuleFor(command => command.TenantId)
                .NotEmpty()
                .WithMessage("TenantId is required.");
        });
        When(_ => currentUser.Role != UserRole.SystemAdmin, () =>
        {
            RuleFor(command => command.TenantId)
                .Must(tenantId => tenantId is null)
                .WithMessage("TenantId cannot be set.");
        });
    }
}

public class CreateAccountCommandHandler(
    IApplicationDbContext db,
    ICurrencyCatalog catalog,
    ICurrentUser currentUser)
    : IRequestHandler<CreateAccountCommand, Guid>
{
    public async Task<Guid> Handle(CreateAccountCommand request, CancellationToken cancellationToken)
    {
        var tenant = await ResolveTenantAsync(request.TenantId, cancellationToken);

        if (!tenant.IsActive)
        {
            throw new TargetDisabledException(
                "tenant",
                tenant.PublicId,
                "Tenant is disabled",
                "Cannot create an account while the tenant is disabled. Enable the tenant first.");
        }

        var customer = await ResolveCustomerAsync(request.CustomerId, tenant.Id, cancellationToken);
        if (customer.Status == UserStatus.Disabled)
        {
            throw new TargetDisabledException(
                "user",
                customer.PublicId,
                "User is disabled",
                "Cannot create an account while the customer is disabled. Enable the customer first.");
        }

        var currency = catalog.TryGet(request.Currency);
        if (currency is null || !currency.IsActive)
        {
            throw new ValidationException([
                new ValidationFailure(nameof(CreateAccountCommand.Currency),
                    "Currency must be an enabled catalog code.")
            ]);
        }

        var custodyMode = Enum.Parse<CustodyMode>(request.CustodyMode!);
        if (!tenant.Allows(custodyMode))
        {
            throw new ValidationException([
                new ValidationFailure(nameof(CreateAccountCommand.CustodyMode),
                    $"The tenant does not allow {custodyMode} accounts.")
            ]);
        }

        // Client money accounts R11, R12: a Custodial account opens only where the firm has a trust bank account.
        if (custodyMode == CustodyMode.Custodial
            && !await db.ClientMoneyAccounts.AsNoTracking().AnyAsync(
                row => row.TenantId == tenant.Id && row.Currency == currency.Code && row.IsActive,
                cancellationToken))
        {
            throw new ValidationException([
                new ValidationFailure(nameof(CreateAccountCommand.Currency),
                    $"The tenant has no active client money account in {currency.Code}, so a Custodial account cannot be opened in {currency.Code}.")
            ]);
        }

        var type = Enum.Parse<AccountType>(request.Type);
        var account = Account.Create(tenant.Id, customer.Id, request.Name, type, custodyMode, currency);
        db.Accounts.Add(account);
        await db.SaveChangesAsync(cancellationToken);
        return account.PublicId;
    }

    private async Task<Tenant> ResolveTenantAsync(Guid? tenantPublicId, CancellationToken cancellationToken)
    {
        if (currentUser.Role == UserRole.SystemAdmin)
        {
            return await db.Tenants.SingleOrDefaultAsync(
                       row => row.PublicId == tenantPublicId, cancellationToken)
                   ?? throw new NotFoundException("Tenant", tenantPublicId!);
        }

        var tenantId = currentUser.TenantId
                       ?? throw new NotFoundException("Tenant", "current");
        return await db.Tenants.SingleOrDefaultAsync(row => row.Id == tenantId, cancellationToken)
               ?? throw new NotFoundException("Tenant", tenantId);
    }

    private async Task<User> ResolveCustomerAsync(
        Guid customerPublicId,
        int tenantId,
        CancellationToken cancellationToken)
    {
        var assignedAdviserId = AccountScope.AssignedAdviserInternalId(currentUser);
        return await db.Users.SingleOrDefaultAsync(
                   row => row.PublicId == customerPublicId
                          && row.Role == UserRole.Customer
                          && row.TenantId == tenantId
                          && (assignedAdviserId == null || row.AdviserId == assignedAdviserId),
                   cancellationToken)
               ?? throw new NotFoundException("Customer", customerPublicId);
    }
}
