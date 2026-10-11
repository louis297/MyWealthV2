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
        var query =
            from transaction in db.Transactions.AsNoTracking()
            join account in db.Accounts.AsNoTracking() on transaction.AccountId equals account.Id
            join customer in db.Users.AsNoTracking() on account.CustomerId equals customer.Id
            join tenant in db.Tenants.AsNoTracking() on transaction.TenantId equals tenant.Id
            where transaction.PublicId == request.Id
                  && (assignedAdviserId == null || customer.AdviserId == assignedAdviserId)
            select new { transaction, account, customer, tenant };

        if (currentUser.Role != UserRole.SystemAdmin)
        {
            query = query.Where(row => row.transaction.TenantId == currentUser.TenantId);
        }

        var row = await query.SingleOrDefaultAsync(cancellationToken)
                  ?? throw new NotFoundException("Transaction", request.Id);

        var cashLeg = await db.TransactionCashLegs.AsNoTracking()
            .SingleOrDefaultAsync(leg => leg.TransactionId == row.transaction.Id, cancellationToken);
        var security = await (
            from leg in db.TransactionSecurityLegs.AsNoTracking()
            join instrument in db.Instruments.AsNoTracking() on leg.InstrumentId equals instrument.Id
            where leg.TransactionId == row.transaction.Id
            orderby leg.Id
            select new TransactionSecurityDto
            {
                InstrumentId = instrument.PublicId,
                Quantity = leg.Quantity,
                Cost = leg.CostAmount,
                QuoteCurrency = leg.CostCurrency.Trim()
            }).ToListAsync(cancellationToken);

        Guid? originalPublicId = null;
        if (row.transaction.OriginalTransactionId is int originalId)
        {
            originalPublicId = await db.Transactions.AsNoTracking()
                .Where(transaction => transaction.Id == originalId)
                .Select(transaction => (Guid?)transaction.PublicId)
                .SingleOrDefaultAsync(cancellationToken);
        }

        return new TransactionDto
        {
            Id = row.transaction.PublicId,
            TenantId = row.tenant.PublicId,
            AccountId = row.account.PublicId,
            CustomerId = row.customer.PublicId,
            Type = row.transaction.Type.ToString(),
            Amount = cashLeg == null ? null : cashLeg.Amount,
            Currency = cashLeg?.Currency,
            BookedAt = TransactionDto.FormatBookedAt(row.transaction.BookedAt),
            Memo = row.transaction.Memo,
            Reference = row.transaction.Reference,
            OriginalTransactionId = originalPublicId,
            RowVersion = Convert.ToBase64String(row.transaction.RowVersion ?? []),
            Security = security.Count == 0 ? null : security
        };
    }
}
