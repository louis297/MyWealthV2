using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Application.Accounts.Commands.UpdateAccount;

[Authorize(Policy = Policies.AccountsManage)]
public class UpdateAccountCommand : IRequest
{
    public Guid Id { get; set; }

    public string? Name { get; init; }

    public string? RowVersion { get; init; }

    public string? Type { get; init; }

    public string? Currency { get; init; }

    public string? Status { get; init; }

    public bool? IsActive { get; init; }

    public Guid? TenantId { get; init; }

    public Guid? CustomerId { get; init; }
}

public class UpdateAccountCommandValidator : AbstractValidator<UpdateAccountCommand>
{
    public UpdateAccountCommandValidator()
    {
        RuleFor(command => command.RowVersion).NotEmpty();
        RuleFor(command => command.Name)
            .Must(name => !string.IsNullOrWhiteSpace(name) && name.Trim().Length is >= 1 and <= 200)
            .WithMessage("Name must be 1 to 200 characters.");
        RuleFor(command => command.Type)
            .Must(type => type is null)
            .WithMessage("Type cannot be changed.");
        RuleFor(command => command.Currency)
            .Must(currency => currency is null)
            .WithMessage("Currency cannot be changed.");
        RuleFor(command => command.Status)
            .Must(status => status is null)
            .WithMessage("Status cannot be changed.");
        RuleFor(command => command.IsActive)
            .Must(isActive => isActive is null)
            .WithMessage("IsActive cannot be changed.");
        RuleFor(command => command.TenantId)
            .Must(tenantId => tenantId is null)
            .WithMessage("TenantId cannot be changed.");
        RuleFor(command => command.CustomerId)
            .Must(customerId => customerId is null)
            .WithMessage("CustomerId cannot be changed.");
    }
}

public class UpdateAccountCommandHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<UpdateAccountCommand>
{
    public async Task Handle(UpdateAccountCommand request, CancellationToken cancellationToken)
    {
        var account = await AccountScope.ManagedAccounts(db, currentUser)
                          .SingleOrDefaultAsync(row => row.PublicId == request.Id, cancellationToken)
                      ?? throw new NotFoundException("Account", request.Id);

        var expected = Convert.ToBase64String(account.RowVersion ?? []);
        if (!string.Equals(expected, request.RowVersion, StringComparison.Ordinal))
        {
            throw new ConcurrencyException();
        }

        account.Rename(request.Name!);
        await db.SaveChangesAsync(cancellationToken);
    }
}
