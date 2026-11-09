using MyWealthV2.Application.Accounts;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Enums;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Application.Transactions.Queries.GetTransactionById;

[Authorize(Policy = Policies.TransactionsRead)]
public record GetTransactionByIdQuery(Guid Id) : IRequest<TransactionDto>;

public class GetTransactionByIdQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetTransactionByIdQuery, TransactionDto>
{
    public async Task<TransactionDto> Handle(GetTransactionByIdQuery request, CancellationToken cancellationToken)
    {
        var assignedAdviserId = AccountScope.AssignedAdviserInternalId(currentUser);
        var selfCustomerId = AccountScope.SelfCustomerId(currentUser);
        var query =
            from transaction in db.Transactions.AsNoTracking()
            join legAccount in TransactionLegAccounts.Query(db) on transaction.Id equals legAccount.TransactionId
            join account in db.Accounts.AsNoTracking() on legAccount.AccountId equals account.Id
            join customer in db.Users.AsNoTracking() on account.CustomerId equals customer.Id
            join tenant in db.Tenants.AsNoTracking() on transaction.TenantId equals tenant.Id
            where transaction.PublicId == request.Id
                  && (assignedAdviserId == null || customer.AdviserId == assignedAdviserId)
                  && (selfCustomerId == null || customer.Id == selfCustomerId)
            select new { transaction, account, customer, tenant };

        if (currentUser.Role != UserRole.SystemAdmin)
        {
            query = query.Where(row => row.transaction.TenantId == currentUser.TenantId);
        }

        var row = await query.SingleOrDefaultAsync(cancellationToken)
                  ?? throw new NotFoundException("Transaction", request.Id);

        var legs = await TransactionLegReader.LoadAsync(db, [row.transaction.Id], cancellationToken);

        Guid? originalPublicId = null;
        if (row.transaction.OriginalTransactionId is int originalId)
        {
            originalPublicId = await db.Transactions.AsNoTracking()
                .Where(transaction => transaction.Id == originalId)
                .Select(transaction => (Guid?)transaction.PublicId)
                .SingleOrDefaultAsync(cancellationToken);
        }

        Guid? reversedByPublicId = null;
        if (row.transaction.ReversedByTransactionId is int reversedById)
        {
            reversedByPublicId = await db.Transactions.AsNoTracking()
                .Where(transaction => transaction.Id == reversedById)
                .Select(transaction => (Guid?)transaction.PublicId)
                .SingleOrDefaultAsync(cancellationToken);
        }

        return new TransactionDto
        {
            Id = row.transaction.PublicId,
            TenantId = row.tenant.PublicId,
            Type = row.transaction.Type.ToString(),
            Cash = legs.Cash[row.transaction.Id].ToList(),
            BookedAt = TransactionDto.FormatBookedAt(row.transaction.BookedAt),
            Memo = row.transaction.Memo,
            Reference = row.transaction.Reference,
            OriginalTransactionId = originalPublicId,
            ReversedByTransactionId = reversedByPublicId,
            RowVersion = Convert.ToBase64String(row.transaction.RowVersion ?? []),
            Security = legs.Security[row.transaction.Id].ToList()
        };
    }
}
