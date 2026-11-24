using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Enums;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Application.InKindTransfers;

/// <summary>In-kind transfers R29: the transfers of the Accounts in the caller's scope, newest requested first.</summary>
[Authorize(Policy = Policies.InKindTransfersRead)]
public sealed record GetInKindTransfersQuery(
    int Page = 1,
    int PageSize = 20,
    Guid? AccountId = null,
    Guid? CustomerId = null,
    Guid? InstrumentId = null,
    string? Direction = null,
    string? Status = null,
    Guid? TenantId = null) : IRequest<PagedList<InKindTransferDto>>
{
    internal static IReadOnlyList<string> StatusNames(string? status) =>
        string.IsNullOrWhiteSpace(status) ? [] : status.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}

/// <summary>In-kind transfers R29: one transfer in the caller's scope.</summary>
[Authorize(Policy = Policies.InKindTransfersRead)]
public sealed record GetInKindTransferByIdQuery(Guid Id) : IRequest<InKindTransferDto>;

public class GetInKindTransfersQueryValidator : AbstractValidator<GetInKindTransfersQuery>
{
    private static readonly string[] Names = Enum.GetNames<InKindTransferStatus>();

    public GetInKindTransfersQueryValidator()
    {
        RuleFor(query => query.Page).GreaterThanOrEqualTo(1);
        RuleFor(query => query.PageSize).InclusiveBetween(1, 100);
        RuleFor(query => query.TenantId).Null().WithMessage("TenantId cannot be set.");
        RuleFor(query => query.Direction)
            .Must(value => value is null or "In" or "Out")
            .WithMessage("Direction must be In or Out.");
        RuleFor(query => query.Status)
            .Must(status => GetInKindTransfersQuery.StatusNames(status).All(name => Names.Contains(name, StringComparer.OrdinalIgnoreCase)))
            .WithMessage($"Status must be one or more of {string.Join(", ", Names)}, comma-separated.");
    }
}

public class GetInKindTransfersQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetInKindTransfersQuery, PagedList<InKindTransferDto>>
{
    public async Task<PagedList<InKindTransferDto>> Handle(GetInKindTransfersQuery request, CancellationToken cancellationToken)
    {
        var query = InKindTransferReader.Scope(db, currentUser).AsNoTracking();
        if (request.AccountId is Guid accountId)
        {
            query = query.Where(row => db.Accounts.Any(account => account.Id == row.AccountId && account.PublicId == accountId));
        }

        if (request.CustomerId is Guid customerId)
        {
            query = query.Where(row => db.Accounts.Any(account => account.Id == row.AccountId
                && db.Users.Any(user => user.Id == account.CustomerId && user.PublicId == customerId)));
        }

        if (request.InstrumentId is Guid instrumentId)
        {
            query = query.Where(row => db.Instruments.Any(instrument => instrument.Id == row.InstrumentId && instrument.PublicId == instrumentId));
        }

        if (request.Direction is not null)
        {
            var direction = Enum.Parse<TransferDirection>(request.Direction);
            query = query.Where(row => row.Direction == direction);
        }

        var statuses = GetInKindTransfersQuery.StatusNames(request.Status)
            .Select(name => Enum.Parse<InKindTransferStatus>(name, ignoreCase: true))
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

        return new PagedList<InKindTransferDto>
        {
            Items = await InKindTransferReader.ToDtosAsync(db, currentUser, rows, cancellationToken),
            Page = request.Page,
            PageSize = request.PageSize,
            TotalCount = totalCount
        };
    }
}

public class GetInKindTransferByIdQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetInKindTransferByIdQuery, InKindTransferDto>
{
    public async Task<InKindTransferDto> Handle(GetInKindTransferByIdQuery request, CancellationToken cancellationToken)
    {
        var row = await InKindTransferReader.Scope(db, currentUser).AsNoTracking()
                      .SingleOrDefaultAsync(item => item.PublicId == request.Id, cancellationToken)
                  ?? throw new NotFoundException("In-kind transfer", request.Id);
        return (await InKindTransferReader.ToDtosAsync(db, currentUser, [row], cancellationToken)).Single();
    }
}
