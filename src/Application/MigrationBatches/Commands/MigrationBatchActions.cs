using System.Text.Json;
using System.Text.Json.Serialization;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;

namespace MyWealthV2.Application.MigrationBatches.Commands;

/// <summary>Migration batches R17: submit a valid <c>Draft</c> for a second person to post.</summary>
[Authorize(Policy = Policies.MigrationBatchesManage)]
public sealed class SubmitMigrationBatchCommand : IRequest
{
    [JsonIgnore]
    public Guid Id { get; set; }

    public string? RowVersion { get; init; }

    public JsonElement? TenantId { get; init; }
}

/// <summary>Migration batches R18: send a <c>Submitted</c> batch back to <c>Draft</c> with a note.</summary>
[Authorize(Policy = Policies.MigrationBatchesManage)]
public sealed class ReturnMigrationBatchCommand : IRequest
{
    [JsonIgnore]
    public Guid Id { get; set; }

    public string? RowVersion { get; init; }

    public string? Note { get; init; }

    public JsonElement? TenantId { get; init; }
}

/// <summary>Migration batches R19: end a batch without posting it; its funding lines go back to suspense.</summary>
[Authorize(Policy = Policies.MigrationBatchesManage)]
public sealed class CancelMigrationBatchCommand : IRequest
{
    [JsonIgnore]
    public Guid Id { get; set; }

    public string? RowVersion { get; init; }

    public string? Note { get; init; }

    public JsonElement? TenantId { get; init; }
}

public class SubmitMigrationBatchCommandHandler : IRequestHandler<SubmitMigrationBatchCommand>
{
    public Task Handle(SubmitMigrationBatchCommand request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}

public class ReturnMigrationBatchCommandHandler : IRequestHandler<ReturnMigrationBatchCommand>
{
    public Task Handle(ReturnMigrationBatchCommand request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}

public class CancelMigrationBatchCommandHandler : IRequestHandler<CancelMigrationBatchCommand>
{
    public Task Handle(CancelMigrationBatchCommand request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
