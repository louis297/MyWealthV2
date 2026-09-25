using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Application.Transactions;
using MyWealthV2.Domain.Enums;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Application.Accounts.Queries.GetAccounts;

[Authorize(Policy = Policies.AccountsRead)]
public record GetAccountsQuery(
    int Page = 1,
    int PageSize = 20,
    string? EnabledOnly = null,
    string? Search = null,
    Guid? TenantId = null,
    Guid? CustomerId = null,
    string? Type = null) : IRequest<PagedList<AccountDto>>;

public class GetAccountsQueryValidator : AbstractValidator<GetAccountsQuery>
{
    public GetAccountsQueryValidator(ICurrentUser currentUser)
    {
        RuleFor(query => query.Page).GreaterThanOrEqualTo(1);
        RuleFor(query => query.PageSize).InclusiveBetween(1, 100);
        RuleFor(query => query.EnabledOnly)
            .Must(value => value is null || bool.TryParse(value, out _))
            .WithMessage("enabledOnly must be true or false.");
        RuleFor(query => query.Type)
            .Must(type => type is null || Enum.TryParse<AccountType>(type, ignoreCase: false, out _))
            .WithMessage("Type must be an account type name.");

        When(_ => currentUser.Role == UserRole.SystemAdmin, () =>
        {
            RuleFor(query => query.TenantId)
                .NotEmpty()
                .WithMessage("TenantId is required.");
        });
        When(_ => currentUser.Role != UserRole.SystemAdmin, () =>
        {
            RuleFor(query => query.TenantId)
                .Must(tenantId => tenantId is null)
                .WithMessage("TenantId cannot be set.");
        });
    }
}

public class GetAccountsQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetAccountsQuery, PagedList<AccountDto>>
{
    public async Task<PagedList<AccountDto>> Handle(
        GetAccountsQuery request,
        CancellationToken cancellationToken)
    {
        int tenantId;
        if (currentUser.Role == UserRole.SystemAdmin)
        {
            var tenant = await db.Tenants.AsNoTracking()
                             .SingleOrDefaultAsync(row => row.PublicId == request.TenantId, cancellationToken)
                         ?? throw new NotFoundException("Tenant", request.TenantId!);
            tenantId = tenant.Id;
        }
        else
        {
            tenantId = currentUser.TenantId
                       ?? throw new NotFoundException("Tenant", "current");
        }

        var assignedAdviserId = AccountScope.AssignedAdviserInternalId(currentUser);
        var query =
            from account in db.Accounts.AsNoTracking()
            join tenant in db.Tenants.AsNoTracking() on account.TenantId equals tenant.Id
            join customer in db.Users.AsNoTracking() on account.CustomerId equals customer.Id
            where account.TenantId == tenantId
                  && (assignedAdviserId == null || customer.AdviserId == assignedAdviserId)
            select new { account, tenant, customer };

        if (bool.TryParse(request.EnabledOnly, out var enabledOnly) && enabledOnly)
        {
            query = query.Where(row => row.account.IsActive);
        }

        if (request.CustomerId is { } customerPublicId)
        {
            query = query.Where(row => row.customer.PublicId == customerPublicId);
        }

        if (request.Type is not null && Enum.TryParse<AccountType>(request.Type, ignoreCase: false, out var type))
        {
            query = query.Where(row => row.account.Type == type);
        }

        var search = request.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            if (Guid.TryParse(search, out var publicId))
            {
                query = query.Where(row =>
                    row.account.PublicId == publicId
                    || row.account.Name.Contains(search));
            }
            else
            {
                query = query.Where(row => row.account.Name.Contains(search));
            }
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(row => row.account.Name)
            .ThenBy(row => row.account.Type)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var accountIds = items.Select(row => row.account.Id).Distinct().ToList();
        var cashBalances = accountIds.Count == 0
            ? new Dictionary<int, decimal>()
            : await (
                from leg in db.TransactionCashLegs.AsNoTracking()
                join transaction in db.Transactions.AsNoTracking() on leg.TransactionId equals transaction.Id
                where accountIds.Contains(transaction.AccountId)
                group leg by transaction.AccountId
                into grouped
                select new { AccountId = grouped.Key, Sum = grouped.Sum(leg => leg.Amount) }
            ).ToDictionaryAsync(row => row.AccountId, row => row.Sum, cancellationToken);

        return new PagedList<AccountDto>
        {
            Items = items.Select(row => AccountDto.From(
                row.account,
                row.tenant,
                row.customer,
                cashBalances.GetValueOrDefault(row.account.Id))).ToList(),
            Page = request.Page,
            PageSize = request.PageSize,
            TotalCount = totalCount
        };
    }
}
