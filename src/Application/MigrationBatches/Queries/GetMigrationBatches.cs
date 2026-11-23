using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;

namespace MyWealthV2.Application.MigrationBatches.Queries;

/// <summary>Migration batches R28: the batches of the caller's Tenant, newest created first.</summary>
[Authorize(Policy = Policies.MigrationBatchesManage)]
public sealed record GetMigrationBatchesQuery(int Page = 1, int PageSize = 20, string? Status = null, Guid? TenantId = null)
    : IRequest<PagedList<MigrationBatchDto>>;

/// <summary>Migration batches R29: one batch with its totals, funding, entries and validation.</summary>
[Authorize(Policy = Policies.MigrationBatchesManage)]
public sealed record GetMigrationBatchByIdQuery(Guid Id) : IRequest<MigrationBatchItemDto>;

public class GetMigrationBatchesQueryHandler
    : IRequestHandler<GetMigrationBatchesQuery, PagedList<MigrationBatchDto>>
{
    public Task<PagedList<MigrationBatchDto>> Handle(GetMigrationBatchesQuery request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}

public class GetMigrationBatchByIdQueryHandler
    : IRequestHandler<GetMigrationBatchByIdQuery, MigrationBatchItemDto>
{
    public Task<MigrationBatchItemDto> Handle(GetMigrationBatchByIdQuery request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
