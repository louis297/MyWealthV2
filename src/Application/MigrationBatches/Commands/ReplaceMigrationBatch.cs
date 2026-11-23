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

public class ReplaceMigrationBatchCommandHandler
    : IRequestHandler<ReplaceMigrationBatchCommand>
{
    public Task Handle(ReplaceMigrationBatchCommand request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
