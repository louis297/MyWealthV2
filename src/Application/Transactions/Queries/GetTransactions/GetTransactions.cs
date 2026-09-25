using MyWealthV2.Application.Accounts;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Enums;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Application.Transactions.Queries.GetTransactions;

[Authorize(Policy = Policies.TransactionsRead)]
public record GetTransactionsQuery(
    int Page = 1,
    int PageSize = 20,
    Guid? TenantId = null,
    Guid? AccountId = null,
    Guid? CustomerId = null) : IRequest<PagedList<TransactionDto>>;

public class GetTransactionsQueryValidator : AbstractValidator<GetTransactionsQuery>
{
    public GetTransactionsQueryValidator(ICurrentUser currentUser)
    {
        RuleFor(query => query.Page).GreaterThanOrEqualTo(1);
        RuleFor(query => query.PageSize).InclusiveBetween(1, 100);

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

public class GetTransactionsQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetTransactionsQuery, PagedList<TransactionDto>>
{
    public async Task<PagedList<TransactionDto>> Handle(
        GetTransactionsQuery request,
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
            from transaction in db.Transactions.AsNoTracking()
            join leg in db.TransactionCashLegs.AsNoTracking() on transaction.Id equals leg.TransactionId
            join account in db.Accounts.AsNoTracking() on transaction.AccountId equals account.Id
            join customer in db.Users.AsNoTracking() on account.CustomerId equals customer.Id
            join tenant in db.Tenants.AsNoTracking() on transaction.TenantId equals tenant.Id
            where transaction.TenantId == tenantId
                  && (assignedAdviserId == null || customer.AdviserId == assignedAdviserId)
            select new { transaction, leg, account, customer, tenant };

        if (request.AccountId is { } accountPublicId)
        {
            query = query.Where(row => row.account.PublicId == accountPublicId);
        }

        if (request.CustomerId is { } customerPublicId)
        {
            query = query.Where(row => row.customer.PublicId == customerPublicId);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderBy(row => row.transaction.BookedAt)
            .ThenBy(row => row.transaction.Id)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var originalIds = rows
            .Where(row => row.transaction.OriginalTransactionId is not null)
            .Select(row => row.transaction.OriginalTransactionId!.Value)
            .Distinct()
            .ToList();
        var originalPublicIds = originalIds.Count == 0
            ? new Dictionary<int, Guid>()
            : await db.Transactions.AsNoTracking()
                .Where(transaction => originalIds.Contains(transaction.Id))
                .ToDictionaryAsync(transaction => transaction.Id, transaction => transaction.PublicId, cancellationToken);

        return new PagedList<TransactionDto>
        {
            Items = rows.Select(row => new TransactionDto
            {
                Id = row.transaction.PublicId,
                TenantId = row.tenant.PublicId,
                AccountId = row.account.PublicId,
                CustomerId = row.customer.PublicId,
                Type = row.transaction.Type.ToString(),
                Amount = row.leg.Amount,
                Currency = row.leg.Currency,
                BookedAt = TransactionDto.FormatBookedAt(row.transaction.BookedAt),
                Memo = row.transaction.Memo,
                Reference = row.transaction.Reference,
                OriginalTransactionId = row.transaction.OriginalTransactionId is int originalId
                    && originalPublicIds.TryGetValue(originalId, out var originalPublicId)
                    ? originalPublicId
                    : null,
                RowVersion = Convert.ToBase64String(row.transaction.RowVersion ?? [])
            }).ToList(),
            Page = request.Page,
            PageSize = request.PageSize,
            TotalCount = totalCount
        };
    }
}
