using System.Data;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Infrastructure.Data.Entities;

namespace MyWealthV2.Infrastructure.Data;

public sealed class OutboxProcessor(IServiceScopeFactory scopes, TimeProvider time)
{
    private const int BatchSize = 20;
    private const int DeadLetterAfter = 5;

    public async Task Process()
    {
        var claimed = await ClaimAsync(time.GetUtcNow());
        foreach (var row in claimed)
        {
            await DeliverAsync(row);
        }
    }

    private async Task<List<Claimed>> ClaimAsync(DateTimeOffset now)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var connection = db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = $"""
            ;WITH due AS (
                SELECT TOP ({BatchSize}) *
                FROM [OutboxMessages] WITH (UPDLOCK, READPAST, ROWLOCK)
                WHERE [Status] = N'Pending'
                   OR ([Status] = N'Failed' AND [RetryCount] < {DeadLetterAfter})
                   OR ([Status] = N'Processing' AND ([LastModified] IS NULL OR [LastModified] < @staleBefore))
                ORDER BY [OccurredAt]
            )
            UPDATE due
            SET [Status] = N'Processing',
                [RetryCount] = CASE WHEN [Status] = N'Processing' THEN [RetryCount] + 1 ELSE [RetryCount] END,
                [LastModified] = @now,
                [LastModifiedBy] = NULL
            OUTPUT inserted.[Id], inserted.[Type], inserted.[Payload],
                   inserted.[TenantId], inserted.[RetryCount], inserted.[RowVersion]
            """;
        AddParameter(command, "@now", now);
        AddParameter(command, "@staleBefore", now.AddMinutes(-5));

        var claimed = new List<Claimed>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                claimed.Add(new Claimed(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetInt32(3),
                    reader.GetInt32(4),
                    (byte[])reader.GetValue(5)));
            }
        }

        await transaction.CommitAsync();
        return claimed;
    }

    private async Task DeliverAsync(Claimed row)
    {
        try
        {
            var domainEvent = OutboxSerializer.Deserialize(row.Type, row.Payload);
            await using var scope = scopes.CreateAsyncScope();
            if (row.TenantId is int tenantId)
            {
                scope.ServiceProvider.GetRequiredService<ITenantIdAccessor>().Engage(tenantId);
            }

            await scope.ServiceProvider.GetRequiredService<IMediator>().Publish(domainEvent);
            var processedAt = time.GetUtcNow();
            await MarkAsync(row, "Processed", row.RetryCount, error: null, processedAt);
        }
        catch (Exception exception)
        {
            var retryCount = row.RetryCount + 1;
            var status = retryCount >= DeadLetterAfter ? "DeadLettered" : "Failed";
            var error = exception.Message.Length <= 2000
                ? exception.Message
                : exception.Message[..2000];
            await MarkAsync(row, status, retryCount, error, processedAt: null);
        }
    }

    private async Task MarkAsync(
        Claimed row,
        string status,
        int retryCount,
        string? error,
        DateTimeOffset? processedAt)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = time.GetUtcNow();
        string? modifiedBy = null;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE [OutboxMessages]
            SET [Status] = {status},
                [ProcessedAt] = {processedAt},
                [Error] = {error},
                [RetryCount] = {retryCount},
                [LastModified] = {now},
                [LastModifiedBy] = {modifiedBy}
            WHERE [Id] = {row.Id} AND [RowVersion] = {row.RowVersion}
            """);
    }

    private static void AddParameter(IDbCommand command, string name, DateTimeOffset value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private sealed record Claimed(
        Guid Id,
        string Type,
        string Payload,
        int? TenantId,
        int RetryCount,
        byte[] RowVersion);
}
