using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Models;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Entities;

namespace MyWealthV2.Application.MigrationBatches.Commands;

/// <summary>Migration batches R6, R7: a TenantAdmin starts a batch as a <c>Draft</c>.</summary>
[Authorize(Policy = Policies.MigrationBatchesManage)]
public sealed class CreateMigrationBatchCommand : MigrationBatchBody, IRequest<CreatedIdDto>;

public class CreateMigrationBatchCommandValidator : AbstractValidator<CreateMigrationBatchCommand>
{
    public CreateMigrationBatchCommandValidator(TimeProvider time) => MigrationBatchBodyRules.Apply(this, time);
}

public class CreateMigrationBatchCommandHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<CreateMigrationBatchCommand, CreatedIdDto>
{
    public async Task<CreatedIdDto> Handle(CreateMigrationBatchCommand request, CancellationToken cancellationToken)
    {
        var tenantId = currentUser.TenantId ?? throw new Common.Exceptions.ForbiddenAccessException();
        var (header, totals, entries) = await MigrationBatchBodyResolver.ResolveAsync(db, tenantId, request, cancellationToken);
        var batch = MigrationBatch.Create(tenantId, header, totals, entries);
        db.MigrationBatches.Add(batch);
        await db.SaveChangesAsync(cancellationToken);
        return new CreatedIdDto(batch.PublicId);
    }
}
