using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.ValueObjects;
using MyWealthV2.Infrastructure.Accounts;

namespace MyWealthV2.Application.FunctionalTests.Accounts;

/// <summary>
/// A save interceptor on the test host. Once armed, just before the next save that adds an Account it inserts
/// another Custodial account on its own connection that clashes with the one being saved: on the deposit
/// reference (two opens that drew the same unused reference, client-money-accounts R17 b), or on the PublicId
/// (a save that fails on another constraint). It then disarms itself.
/// </summary>
public sealed class CollidingAccountSaveInterceptor : SaveChangesInterceptor
{
    public enum Clash
    {
        None,
        DepositReference,
        PublicId
    }

    private int _armed;

    /// <summary>The deposit reference of the account this interceptor inserted, if it inserted one.</summary>
    public string? InsertedReference { get; private set; }

    public void Arm(Clash clash)
    {
        InsertedReference = null;
        Interlocked.Exchange(ref _armed, (int)clash);
    }

    public void Reset() => Arm(Clash.None);

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        var context = eventData.Context;
        var added = context?.ChangeTracker.Entries<Account>()
            .FirstOrDefault(entry => entry.State == EntityState.Added)?.Entity;
        if (context is not null && added is not null)
        {
            var clash = (Clash)Interlocked.Exchange(ref _armed, (int)Clash.None);
            if (clash != Clash.None)
            {
                await InsertClashAsync(context.Database.GetConnectionString()!, added, clash, cancellationToken);
            }
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private async Task InsertClashAsync(string connectionString, Account added, Clash clash, CancellationToken cancellationToken)
    {
        var reference = clash == Clash.DepositReference
            ? added.DepositReference!.Value
            : DepositReference.Generate(CryptoRandomSymbols.Instance).Value;
        var publicId = clash == Clash.PublicId ? added.PublicId : Guid.NewGuid();

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO [Accounts] ([PublicId], [TenantId], [CustomerId], [Name], [Type], [CustodyMode], [Status], [Currency], [DepositReference], [Created], [CreatedBy])
            VALUES (@publicId, @tenantId, @customerId, N'Clashing account', 2, 1, 0, @currency, @reference, SYSDATETIMEOFFSET(), N'test');
            """;
        command.Parameters.AddWithValue("@publicId", publicId);
        command.Parameters.AddWithValue("@tenantId", added.TenantId);
        command.Parameters.AddWithValue("@customerId", added.CustomerId);
        command.Parameters.AddWithValue("@currency", added.Currency);
        command.Parameters.AddWithValue("@reference", reference);
        await command.ExecuteNonQueryAsync(cancellationToken);
        InsertedReference = reference;
    }
}
