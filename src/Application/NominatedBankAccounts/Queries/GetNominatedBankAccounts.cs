using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.NominatedBankAccounts.Queries;

/// <summary>Withdrawals R13: the caller's nominated bank accounts, newest added first.</summary>
[Authorize(Policy = Policies.NominatedBankAccountsRead)]
public sealed record GetNominatedBankAccountsQuery(
    int Page = 1,
    int PageSize = 20,
    Guid? CustomerId = null,
    string? Status = null,
    string? Currency = null,
    Guid? TenantId = null) : IRequest<PagedList<NominatedBankAccountDto>>
{
    internal static IReadOnlyList<string> StatusNames(string? status) =>
        string.IsNullOrWhiteSpace(status)
            ? []
            : status.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}

/// <summary>Withdrawals R13: one nominated bank account in the caller's scope.</summary>
[Authorize(Policy = Policies.NominatedBankAccountsRead)]
public sealed record GetNominatedBankAccountByIdQuery(Guid Id) : IRequest<NominatedBankAccountDto>;

public class GetNominatedBankAccountsQueryValidator : AbstractValidator<GetNominatedBankAccountsQuery>
{
    private static readonly string[] Names = Enum.GetNames<NominatedBankAccountStatus>();

    public GetNominatedBankAccountsQueryValidator()
    {
        RuleFor(query => query.Page).GreaterThanOrEqualTo(1);
        RuleFor(query => query.PageSize).InclusiveBetween(1, 100);
        RuleFor(query => query.TenantId).Null().WithMessage("TenantId cannot be set.");
        RuleFor(query => query.Status)
            .Must(status => GetNominatedBankAccountsQuery.StatusNames(status)
                .All(name => Names.Contains(name, StringComparer.OrdinalIgnoreCase)))
            .WithMessage($"Status must be one or more of {string.Join(", ", Names)}, comma-separated.");
    }
}

public class GetNominatedBankAccountsQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetNominatedBankAccountsQuery, PagedList<NominatedBankAccountDto>>
{
    public async Task<PagedList<NominatedBankAccountDto>> Handle(
        GetNominatedBankAccountsQuery request,
        CancellationToken cancellationToken)
    {
        var query = NominatedBankAccountReader.Scope(db, currentUser).AsNoTracking();
        if (request.CustomerId is Guid customerId)
        {
            query = query.Where(row => db.Users.Any(user => user.Id == row.CustomerId && user.PublicId == customerId));
        }

        var statuses = GetNominatedBankAccountsQuery.StatusNames(request.Status)
            .Select(name => Enum.Parse<NominatedBankAccountStatus>(name, ignoreCase: true))
            .Distinct()
            .ToList();
        if (statuses.Count > 0)
        {
            query = query.Where(row => statuses.Contains(row.Status));
        }

        if (!string.IsNullOrWhiteSpace(request.Currency))
        {
            var currency = request.Currency.Trim().ToUpperInvariant();
            query = query.Where(row => row.Currency == currency);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(row => row.AddedAt)
            .ThenByDescending(row => row.Id)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedList<NominatedBankAccountDto>
        {
            Items = await NominatedBankAccountReader.ToDtosAsync(db, currentUser, rows, cancellationToken),
            Page = request.Page,
            PageSize = request.PageSize,
            TotalCount = totalCount
        };
    }
}

public class GetNominatedBankAccountByIdQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetNominatedBankAccountByIdQuery, NominatedBankAccountDto>
{
    public async Task<NominatedBankAccountDto> Handle(GetNominatedBankAccountByIdQuery request, CancellationToken cancellationToken)
    {
        var row = await NominatedBankAccountReader.Scope(db, currentUser).AsNoTracking()
                      .SingleOrDefaultAsync(item => item.PublicId == request.Id, cancellationToken)
                  ?? throw new Common.Exceptions.NotFoundException("Nominated bank account", request.Id);
        return (await NominatedBankAccountReader.ToDtosAsync(db, currentUser, [row], cancellationToken)).Single();
    }
}
