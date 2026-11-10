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
    Guid? CustomerId = null,
    string? Type = null) : IRequest<PagedList<TransactionDto>>;

public class GetTransactionsQueryValidator : AbstractValidator<GetTransactionsQuery>
{
    public GetTransactionsQueryValidator(ICurrentUser currentUser)
    {
        RuleFor(query => query.Page).GreaterThanOrEqualTo(1);
        RuleFor(query => query.PageSize).InclusiveBetween(1, 100);
        RuleFor(query => query.Type)
            .Must(type => type is null || Enum.GetNames<TransactionType>().Contains(type, StringComparer.Ordinal))
            .WithMessage("Type must be a transaction type name.");

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

        var selfCustomerId = AccountScope.SelfCustomerId(currentUser);
        var visibleAccountIds = TransactionVisibility.VisibleAccountIds(db, currentUser, tenantId);
        var transactions = db.Transactions.AsNoTracking()
            .Where(transaction => transaction.TenantId == tenantId)
            .WithLegOn(visibleAccountIds);

        if (request.AccountId is { } accountPublicId)
        {
            if (selfCustomerId is int ownCustomerId)
            {
                var visible = await (
                    from account in db.Accounts.AsNoTracking()
                    where account.PublicId == accountPublicId
                          && account.TenantId == tenantId
                          && account.CustomerId == ownCustomerId
                    select account.Id).AnyAsync(cancellationToken);
                if (!visible)
                {
                    throw new NotFoundException("Account", accountPublicId);
                }
            }

            transactions = transactions.WithLegOn(
                db.Accounts.Where(account => account.PublicId == accountPublicId).Select(account => account.Id));
        }

        if (request.CustomerId is { } customerPublicId)
        {
            if (selfCustomerId is not null && customerPublicId != currentUser.PublicId)
            {
                throw new NotFoundException("Customer", customerPublicId);
            }

            transactions = transactions.WithLegOn(
                from account in db.Accounts
                join customer in db.Users on account.CustomerId equals customer.Id
                where customer.PublicId == customerPublicId
                select account.Id);
        }

        if (request.Type is not null)
        {
            var type = Enum.Parse<TransactionType>(request.Type);
            transactions = transactions.Where(transaction => transaction.Type == type);
        }

        var query =
            from transaction in transactions
            join tenant in db.Tenants.AsNoTracking() on transaction.TenantId equals tenant.Id
            select new { transaction, tenant };

        var totalCount = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderBy(row => row.transaction.BookedAt)
            .ThenBy(row => row.transaction.Id)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var transactionIds = rows.Select(row => row.transaction.Id).ToList();
        var legs = await TransactionLegReader.LoadAsync(db, transactionIds, visibleAccountIds, cancellationToken);

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

        var reversedByIds = rows
            .Where(row => row.transaction.ReversedByTransactionId is not null)
            .Select(row => row.transaction.ReversedByTransactionId!.Value)
            .Distinct()
            .ToList();
        var reversedByPublicIds = reversedByIds.Count == 0
            ? new Dictionary<int, Guid>()
            : await db.Transactions.AsNoTracking()
                .Where(transaction => reversedByIds.Contains(transaction.Id))
                .ToDictionaryAsync(transaction => transaction.Id, transaction => transaction.PublicId, cancellationToken);

        return new PagedList<TransactionDto>
        {
            Items = rows.Select(row =>
            {
                return new TransactionDto
                {
                    Id = row.transaction.PublicId,
                    TenantId = row.tenant.PublicId,
                    Type = row.transaction.Type.ToString(),
                    Cash = legs.Cash[row.transaction.Id].ToList(),
                    BookedAt = TransactionDto.FormatBookedAt(row.transaction.BookedAt),
                    Memo = row.transaction.Memo,
                    Reference = row.transaction.Reference,
                    OriginalTransactionId = row.transaction.OriginalTransactionId is int originalId
                        && originalPublicIds.TryGetValue(originalId, out var originalPublicId)
                        ? originalPublicId
                        : null,
                    ReversedByTransactionId = row.transaction.ReversedByTransactionId is int reversedById
                        && reversedByPublicIds.TryGetValue(reversedById, out var reversedByPublicId)
                        ? reversedByPublicId
                        : null,
                    RowVersion = Convert.ToBase64String(row.transaction.RowVersion ?? []),
                    Security = legs.Security[row.transaction.Id].ToList(),
                    Fx = legs.Fx.GetValueOrDefault(row.transaction.Id)
                };
            }).ToList(),
            Page = request.Page,
            PageSize = request.PageSize,
            TotalCount = totalCount
        };
    }
}
