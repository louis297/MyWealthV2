using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Enums;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Application.Withdrawals.Queries;

/// <summary>Withdrawals R26: the withdrawals of the Accounts in the caller's scope, newest requested first.</summary>
[Authorize(Policy = Policies.WithdrawalsRead)]
public sealed record GetWithdrawalsQuery(
    int Page = 1,
    int PageSize = 20,
    Guid? AccountId = null,
    Guid? CustomerId = null,
    string? Status = null,
    Guid? TenantId = null) : IRequest<PagedList<WithdrawalDto>>
{
    internal static IReadOnlyList<string> StatusNames(string? status) =>
        string.IsNullOrWhiteSpace(status)
            ? []
            : status.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}

/// <summary>Withdrawals R26: one withdrawal in the caller's scope.</summary>
[Authorize(Policy = Policies.WithdrawalsRead)]
public sealed record GetWithdrawalByIdQuery(Guid Id) : IRequest<WithdrawalDto>;

public class GetWithdrawalsQueryValidator : AbstractValidator<GetWithdrawalsQuery>
{
    private static readonly string[] Names = Enum.GetNames<WithdrawalStatus>();

    public GetWithdrawalsQueryValidator()
    {
        RuleFor(query => query.Page).GreaterThanOrEqualTo(1);
        RuleFor(query => query.PageSize).InclusiveBetween(1, 100);
        RuleFor(query => query.TenantId).Null().WithMessage("TenantId cannot be set.");
        RuleFor(query => query.Status)
            .Must(status => GetWithdrawalsQuery.StatusNames(status)
                .All(name => Names.Contains(name, StringComparer.OrdinalIgnoreCase)))
            .WithMessage($"Status must be one or more of {string.Join(", ", Names)}, comma-separated.");
    }
}

public class GetWithdrawalsQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetWithdrawalsQuery, PagedList<WithdrawalDto>>
{
    public async Task<PagedList<WithdrawalDto>> Handle(GetWithdrawalsQuery request, CancellationToken cancellationToken)
    {
        var query = WithdrawalReader.Scope(db, currentUser).AsNoTracking();
        if (request.AccountId is Guid accountId)
        {
            query = query.Where(row => db.Accounts.Any(account => account.Id == row.AccountId && account.PublicId == accountId));
        }

        if (request.CustomerId is Guid customerId)
        {
            query = query.Where(row => db.Accounts.Any(account => account.Id == row.AccountId
                && db.Users.Any(user => user.Id == account.CustomerId && user.PublicId == customerId)));
        }

        var statuses = GetWithdrawalsQuery.StatusNames(request.Status)
            .Select(name => Enum.Parse<WithdrawalStatus>(name, ignoreCase: true))
            .Distinct()
            .ToList();
        if (statuses.Count > 0)
        {
            query = query.Where(row => statuses.Contains(row.Status));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(row => row.RequestedAt)
            .ThenByDescending(row => row.Id)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedList<WithdrawalDto>
        {
            Items = await WithdrawalReader.ToDtosAsync(db, currentUser, rows, cancellationToken),
            Page = request.Page,
            PageSize = request.PageSize,
            TotalCount = totalCount
        };
    }
}

public class GetWithdrawalByIdQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetWithdrawalByIdQuery, WithdrawalDto>
{
    public async Task<WithdrawalDto> Handle(GetWithdrawalByIdQuery request, CancellationToken cancellationToken)
    {
        var row = await WithdrawalReader.Scope(db, currentUser).AsNoTracking()
                      .SingleOrDefaultAsync(item => item.PublicId == request.Id, cancellationToken)
                  ?? throw new NotFoundException("Withdrawal", request.Id);
        return (await WithdrawalReader.ToDtosAsync(db, currentUser, [row], cancellationToken)).Single();
    }
}
