using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Enums;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Application.MigrationBatches.Queries;

/// <summary>Migration batches R28: the batches of the caller's Tenant, newest created first.</summary>
[Authorize(Policy = Policies.MigrationBatchesManage)]
public sealed record GetMigrationBatchesQuery(int Page = 1, int PageSize = 20, string? Status = null, Guid? TenantId = null)
    : IRequest<PagedList<MigrationBatchDto>>
{
    internal static IReadOnlyList<string> Names(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}

/// <summary>Migration batches R29: one batch with its totals, funding, entries and validation.</summary>
[Authorize(Policy = Policies.MigrationBatchesManage)]
public sealed record GetMigrationBatchByIdQuery(Guid Id) : IRequest<MigrationBatchItemDto>;

public class GetMigrationBatchesQueryValidator : AbstractValidator<GetMigrationBatchesQuery>
{
    private static readonly string[] Statuses = Enum.GetNames<MigrationBatchStatus>();

    public GetMigrationBatchesQueryValidator()
    {
        RuleFor(query => query.Page).GreaterThanOrEqualTo(1);
        RuleFor(query => query.PageSize).InclusiveBetween(1, 100);
        RuleFor(query => query.TenantId).Null().WithMessage("TenantId cannot be set.");
        RuleFor(query => query.Status)
            .Must(status => GetMigrationBatchesQuery.Names(status).All(name => Statuses.Contains(name, StringComparer.OrdinalIgnoreCase)))
            .WithMessage($"Status must be one or more of {string.Join(", ", Statuses)}, comma-separated.");
    }
}

public class GetMigrationBatchesQueryHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetMigrationBatchesQuery, PagedList<MigrationBatchDto>>
{
    public async Task<PagedList<MigrationBatchDto>> Handle(GetMigrationBatchesQuery request, CancellationToken cancellationToken)
    {
        var query = MigrationBatchRules.Scope(db, currentUser).AsNoTracking();
        var statuses = GetMigrationBatchesQuery.Names(request.Status)
            .Select(name => Enum.Parse<MigrationBatchStatus>(name, ignoreCase: true))
            .Distinct()
            .ToList();
        if (statuses.Count > 0)
        {
            query = query.Where(batch => statuses.Contains(batch.Status));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(batch => batch.Created)
            .ThenByDescending(batch => batch.Id)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);
        return new PagedList<MigrationBatchDto>
        {
            Items = await MigrationBatchReader.ToDtosAsync(db, rows, cancellationToken),
            Page = request.Page,
            PageSize = request.PageSize,
            TotalCount = totalCount
        };
    }
}

public class GetMigrationBatchByIdQueryHandler(IApplicationDbContext db, ICurrentUser currentUser, ICurrencyCatalog catalog)
    : IRequestHandler<GetMigrationBatchByIdQuery, MigrationBatchItemDto>
{
    public async Task<MigrationBatchItemDto> Handle(GetMigrationBatchByIdQuery request, CancellationToken cancellationToken)
    {
        var batch = await MigrationBatchRules.WithBody(MigrationBatchRules.Scope(db, currentUser).AsNoTracking())
                        .SingleOrDefaultAsync(row => row.PublicId == request.Id, cancellationToken)
                    ?? throw new NotFoundException("MigrationBatch", request.Id);
        return await MigrationBatchReader.ToItemAsync(db, catalog, batch, cancellationToken);
    }
}
