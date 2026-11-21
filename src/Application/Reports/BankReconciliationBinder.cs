using System.Text.Json;
using System.Text.Json.Serialization;
using FluentValidation.Results;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Entities;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.Reports;

/// <summary>Bank-reconciliation R24: the parameters of report <c>bank-reconciliation</c> and their scope check.</summary>
public static class BankReconciliationBinder
{
    public const string Code = "bank-reconciliation";

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static BankReconciliationBinding Bind(JsonElement parameters)
    {
        BankReconciliationInput input;
        try
        {
            input = parameters.Deserialize<BankReconciliationInput>(Options) ?? throw Invalid();
        }
        catch (JsonException)
        {
            throw Invalid();
        }

        if (input.ClientMoneyAccountId is not Guid clientMoneyAccountId || clientMoneyAccountId == Guid.Empty)
        {
            throw new ValidationException([
                new ValidationFailure("clientMoneyAccountId", "clientMoneyAccountId is required.")
            ]);
        }

        var reconciliationId = input.ReconciliationId == Guid.Empty ? null : input.ReconciliationId;
        var snapshot = JsonSerializer.Serialize(new BankReconciliationInput(clientMoneyAccountId, reconciliationId), Options);
        return new BankReconciliationBinding(clientMoneyAccountId, reconciliationId, snapshot);
    }

    /// <summary>CheckScope: the client money account is the Tenant's, and the run, when named, is one of it (else 404).</summary>
    public static async Task<ClientMoneyAccount> EnsureScopeAsync(
        IApplicationDbContext db,
        int tenantId,
        BankReconciliationBinding binding,
        CancellationToken cancellationToken)
    {
        var account = await db.ClientMoneyAccounts.AsNoTracking()
                          .SingleOrDefaultAsync(
                              row => row.PublicId == binding.ClientMoneyAccountId && row.TenantId == tenantId,
                              cancellationToken)
                      ?? throw new NotFoundException("Client money account", binding.ClientMoneyAccountId);
        if (binding.ReconciliationId is Guid runId
            && !await db.BankReconciliations.AsNoTracking().AnyAsync(
                row => row.PublicId == runId && row.TenantId == tenantId && row.ClientMoneyAccountId == account.Id,
                cancellationToken))
        {
            throw new NotFoundException("Bank reconciliation", runId);
        }

        return account;
    }

    private static ValidationException Invalid() => new([
        new ValidationFailure("parameters", "parameters are not valid.")
    ]);

    private sealed record BankReconciliationInput(
        [property: JsonPropertyName("clientMoneyAccountId")] Guid? ClientMoneyAccountId,
        [property: JsonPropertyName("reconciliationId")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        Guid? ReconciliationId);
}

public sealed record BankReconciliationBinding(Guid ClientMoneyAccountId, Guid? ReconciliationId, string SnapshotJson);
