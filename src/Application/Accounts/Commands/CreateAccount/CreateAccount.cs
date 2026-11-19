using FluentValidation.Results;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.ValueObjects;
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

    /// <summary>Refused when present: the server draws the reference (client-money-accounts R16).</summary>
    public string? DepositReference { get; init; }
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
        RuleFor(command => command.DepositReference)
            .Must(reference => reference is null)
            .WithMessage("DepositReference cannot be set. The server draws it.");
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
    ICurrentUser currentUser,
    IDepositReferenceSource depositReferences)
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
        if (custodyMode == CustodyMode.External)
        {
            var external = Account.Create(tenant.Id, customer.Id, request.Name, type, custodyMode, currency);
            db.Accounts.Add(external);
            await db.SaveChangesAsync(cancellationToken);
            return external.PublicId;
        }

        // Client money accounts R17: the reference is unique on the platform. Draw again when it is already used
        // (a), and build the account again when the save hits the unique index because another open drew the same
        // reference at the same time (b). Every draw counts towards MaxReferenceDraws.
        var draws = 0;
        while (true)
        {
            var depositReference = await DrawUnusedReferenceAsync(() => ++draws, cancellationToken);
            var account = Account.Create(
                tenant.Id, customer.Id, request.Name, type, custodyMode, currency, depositReference);
            db.Accounts.Add(account);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return account.PublicId;
            }
            catch (DepositReferenceTakenException) when (draws < MaxReferenceDraws)
            {
                db.ClearTrackedChanges();
            }
        }
    }

    /// <summary>At most five draws per request (client-money-accounts R17).</summary>
    private const int MaxReferenceDraws = 5;

    /// <summary>
    /// Draws until the reference is unused in every tenant (R17 a). <paramref name="countDraw"/> counts each draw
    /// and returns the running total; the draw that reaches <see cref="MaxReferenceDraws"/> is the last.
    /// </summary>
    private async Task<DepositReference> DrawUnusedReferenceAsync(Func<int> countDraw, CancellationToken cancellationToken)
    {
        while (true)
        {
            var draw = countDraw();
            var reference = depositReferences.Next();
            var used = await db.Accounts.IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(row => row.DepositReference! == reference, cancellationToken);
            if (!used)
            {
                return reference;
            }

            if (draw >= MaxReferenceDraws)
            {
                throw new InvalidOperationException(
                    $"{MaxReferenceDraws} deposit references in a row were already used.");
            }
        }
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
