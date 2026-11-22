using MyWealthV2.Application.Accounts;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Application.Interest.Accruals;

/// <summary>Interest-run R36: an account's daily accrual rows, newest day first. An External account has none.</summary>
[Authorize(Policy = Policies.AccountsRead)]
public record GetInterestAccrualsQuery(
    Guid AccountId,
    int Page = 1,
    int PageSize = 20,
    DateOnly? From = null,
    DateOnly? To = null,
    Guid? TransactionId = null,
    bool? Posted = null) : IRequest<PagedList<InterestAccrualDto>>;

/// <summary>Interest-run R36: one daily accrual row.</summary>
public sealed record InterestAccrualDto(
    DateOnly Date,
    decimal Balance,
    decimal RatePercent,
    Guid ScheduleId,
    decimal Amount,
    Guid? TransactionId);

public class GetInterestAccrualsQueryValidator : AbstractValidator<GetInterestAccrualsQuery>
{
    public GetInterestAccrualsQueryValidator()
    {
        RuleFor(query => query.Page).GreaterThanOrEqualTo(1);
        RuleFor(query => query.PageSize).InclusiveBetween(1, 100);
        RuleFor(query => query.To)
            .Must((query, to) => to is null || query.From is null || to >= query.From)
            .WithMessage("To must not be before From.");
    }
}

public class GetInterestAccrualsQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetInterestAccrualsQuery, PagedList<InterestAccrualDto>>
{
    public async Task<PagedList<InterestAccrualDto>> Handle(GetInterestAccrualsQuery request, CancellationToken cancellationToken)
    {
        var account = await AccountScope.ManagedAccounts(db, currentUser).AsNoTracking()
                          .Select(row => new { row.Id, row.PublicId })
                          .SingleOrDefaultAsync(row => row.PublicId == request.AccountId, cancellationToken)
                      ?? throw new NotFoundException("Account", request.AccountId);

        var query =
            from row in db.InterestAccruals.AsNoTracking()
            join schedule in db.InterestRateSchedules.AsNoTracking() on row.InterestRateScheduleId equals schedule.Id
            join transaction in db.Transactions.AsNoTracking() on row.PostedTransactionId equals transaction.Id into posted
            from transaction in posted.DefaultIfEmpty()
            where row.AccountId == account.Id
            select new { row, ScheduleId = schedule.PublicId, TransactionId = transaction == null ? (Guid?)null : transaction.PublicId };
        if (request.From is { } from)
        {
            query = query.Where(item => item.row.Date >= from);
        }

        if (request.To is { } to)
        {
            query = query.Where(item => item.row.Date <= to);
        }

        if (request.TransactionId is { } transactionId)
        {
            query = query.Where(item => item.TransactionId == transactionId);
        }

        if (request.Posted is { } isPosted)
        {
            query = isPosted
                ? query.Where(item => item.row.PostedTransactionId != null)
                : query.Where(item => item.row.PostedTransactionId == null);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(item => item.row.Date)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(item => new InterestAccrualDto(
                item.row.Date, item.row.Balance, item.row.EffectiveRatePercent, item.ScheduleId, item.row.Amount, item.TransactionId))
            .ToListAsync(cancellationToken);
        return new PagedList<InterestAccrualDto>
        {
            Items = items,
            Page = request.Page,
            PageSize = request.PageSize,
            TotalCount = totalCount
        };
    }
}
