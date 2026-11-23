using System.Text.Json.Serialization;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;

namespace MyWealthV2.Application.MigrationBatches.Commands;

/// <summary>Migration batches R6, R7: a TenantAdmin replaces the header, the control totals and every entry of a
/// <c>Draft</c>.</summary>
[Authorize(Policy = Policies.MigrationBatchesManage)]
public sealed class ReplaceMigrationBatchCommand : MigrationBatchBody, IRequest
{
    [JsonIgnore]
    public Guid Id { get; set; }

    public string? RowVersion { get; init; }
}

public class ReplaceMigrationBatchCommandValidator : AbstractValidator<ReplaceMigrationBatchCommand>
{
    public ReplaceMigrationBatchCommandValidator(TimeProvider time)
    {
        RuleFor(command => command.RowVersion).NotEmpty();
        MigrationBatchBodyRules.Apply(this, time);
    }
}

public class ReplaceMigrationBatchCommandHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<ReplaceMigrationBatchCommand>
{
    public async Task Handle(ReplaceMigrationBatchCommand request, CancellationToken cancellationToken)
    {
        var batch = await MigrationBatchRules.FindAsync(db, currentUser, request.Id, request.RowVersion, cancellationToken, withBody: true);
        var (header, totals, entries) = await MigrationBatchBodyResolver.ResolveAsync(db, batch.TenantId, request, cancellationToken);
        MigrationBatchRules.Guard(() => batch.Replace(header, totals, entries));
        await MigrationBatchRules.SaveAsync(db, cancellationToken);
    }
}
