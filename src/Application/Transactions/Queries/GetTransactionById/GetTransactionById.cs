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
        var header = db.Transactions.AsNoTracking().Where(transaction => transaction.PublicId == request.Id);
        if (currentUser.Role != UserRole.SystemAdmin)
        {
            header = header.Where(transaction => transaction.TenantId == currentUser.TenantId);
        }

        var row = await (
                      from transaction in header
                      join tenant in db.Tenants.AsNoTracking() on transaction.TenantId equals tenant.Id
                      select new { transaction, tenant }).SingleOrDefaultAsync(cancellationToken)
                  ?? throw new NotFoundException("Transaction", request.Id);

        var visibleAccountIds = TransactionVisibility.VisibleAccountIds(db, currentUser, row.transaction.TenantId);
        var legs = await TransactionLegReader.LoadAsync(db, [row.transaction.Id], visibleAccountIds, cancellationToken);
        if (!legs.Cash[row.transaction.Id].Any() && !legs.Security[row.transaction.Id].Any())
        {
            throw new NotFoundException("Transaction", request.Id);
        }

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
            BookedAt = row.transaction.BookedAt,
            Memo = row.transaction.Memo,
            Reference = row.transaction.Reference,
            OriginalTransactionId = originalPublicId,
            ReversedByTransactionId = reversedByPublicId,
            RowVersion = Convert.ToBase64String(row.transaction.RowVersion ?? []),
            Security = legs.Security[row.transaction.Id].ToList(),
            Fx = legs.Fx.GetValueOrDefault(row.transaction.Id)
        };
    }
}
