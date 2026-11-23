using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;

namespace MyWealthV2.Application.MigrationBatches.Commands;

/// <summary>Migration batches R6, R7: a TenantAdmin starts a batch as a <c>Draft</c>.</summary>
[Authorize(Policy = Policies.MigrationBatchesManage)]
public sealed class CreateMigrationBatchCommand : MigrationBatchBody, IRequest<CreatedIdDto>;

public class CreateMigrationBatchCommandHandler
    : IRequestHandler<CreateMigrationBatchCommand, CreatedIdDto>
{
    public Task<CreatedIdDto> Handle(CreateMigrationBatchCommand request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
