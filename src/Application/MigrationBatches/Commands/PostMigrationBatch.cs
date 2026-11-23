using System.Text.Json;
using System.Text.Json.Serialization;
using MyWealthV2.Application.Common.Security;

namespace MyWealthV2.Application.MigrationBatches.Commands;

/// <summary>Migration batches R20–R24: a second TenantAdmin posts a <c>Submitted</c> batch, all entries or none.</summary>
[Authorize(Policy = Policies.MigrationBatchesManage)]
public sealed class PostMigrationBatchCommand : IRequest
{
    [JsonIgnore]
    public Guid Id { get; set; }

    public string? RowVersion { get; init; }

    public JsonElement? TenantId { get; init; }
}

public class PostMigrationBatchCommandHandler : IRequestHandler<PostMigrationBatchCommand>
{
    public Task Handle(PostMigrationBatchCommand request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
