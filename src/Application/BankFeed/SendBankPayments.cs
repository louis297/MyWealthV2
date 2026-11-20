using Microsoft.Extensions.Logging;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.BankFeed;

/// <summary>
/// The send step of <c>bank-statement-sync:{connection}</c> (docs/features/withdrawals.md R32–R34; ADR 0021 rules
/// 12, 13): every <c>Pending</c> outgoing payment of the connection whose client money account is active, oldest
/// first. Each attempt is recorded and committed before the call; each outcome is one database transaction guarded by
/// the payment's row version. A payment's failure never stops the others.
/// </summary>
public sealed class SendBankPayments(
    IApplicationDbContext db,
    IBankPayments payments,
    IBankConnections connections,
    TimeProvider time,
    ILogger<SendBankPayments> logger)
{
    public async Task RunAsync(string connection, CancellationToken cancellationToken = default)
    {
        if (!connections.IsConfigured(connection))
        {
            logger.LogWarning("Bank connection {Connection} is not configured on this host; nothing is sent.", connection);
            return;
        }

        var ids = await (
                from payment in db.OutgoingPayments.IgnoreQueryFilters().AsNoTracking()
                join clientMoney in db.ClientMoneyAccounts.IgnoreQueryFilters().AsNoTracking()
                    on payment.ClientMoneyAccountId equals clientMoney.Id
                join tenant in db.Tenants.AsNoTracking() on payment.TenantId equals tenant.Id
                where payment.Connection == connection
                      && payment.Status == OutgoingPaymentStatus.Pending
                      && clientMoney.IsActive
                      // R32: a disabled Tenant gets no first attempt; an attempted payment is sent again.
                      && (tenant.IsActive || payment.Attempts > 0)
                orderby payment.Id
                select payment.Id)
            .ToListAsync(cancellationToken);

        foreach (var id in ids)
        {
            try
            {
                await SendAsync(id, cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Another writer changed the payment (a cancel, or a second run); the next run sees it again if it is
                // still Pending.
                logger.LogWarning("Outgoing payment {PaymentId} changed while it was sent; it is skipped.", id);
            }
        }
    }

    private async Task SendAsync(int id, CancellationToken cancellationToken)
    {
        db.ClearTrackedChanges();
        var payment = await db.OutgoingPayments.IgnoreQueryFilters().SingleAsync(row => row.Id == id, cancellationToken);
        if (payment.Status != OutgoingPaymentStatus.Pending)
        {
            return;
        }

        var request = await RequestAsync(payment, cancellationToken);
        payment.RecordAttempt(time.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);

        var result = await payments.SendAsync(request, cancellationToken);

        await db.ExecuteInTransactionAsync(async token =>
        {
            var withdrawal = await db.Withdrawals.IgnoreQueryFilters().SingleAsync(row => row.Id == payment.WithdrawalId, token);
            if (result.PaymentId is { } paymentId)
            {
                payment.Accept(paymentId, time.GetUtcNow());
                withdrawal.MarkSent();
            }
            else if (result.RejectReason is { } reason)
            {
                payment.Reject(reason);
                withdrawal.MarkFailed(reason);
            }
            else
            {
                payment.RecordFailure(result.Failure!.Value);
                logger.LogError(
                    "Outgoing payment {Instruction} failed on connection {Connection}: {Failure}; it is sent again on the next run.",
                    payment.InstructionPublicId, payment.Connection, result.Failure);
            }

            await db.SaveChangesAsync(token);
        }, cancellationToken);
    }

    /// <summary>R58: the call is built from stored data only, so every attempt sends the same body.</summary>
    private async Task<BankPaymentRequest> RequestAsync(OutgoingPayment payment, CancellationToken cancellationToken)
    {
        var trustAccount = await db.ClientMoneyAccounts.IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.Id == payment.ClientMoneyAccountId)
            .Select(row => row.AccountNumber)
            .SingleAsync(cancellationToken);
        var payee = await (
                from withdrawal in db.Withdrawals.IgnoreQueryFilters().AsNoTracking()
                join nominated in db.NominatedBankAccounts.IgnoreQueryFilters().AsNoTracking()
                    on withdrawal.NominatedBankAccountId equals nominated.Id
                where withdrawal.Id == payment.WithdrawalId
                select new BankPaymentPayee(nominated.AccountNumber, nominated.AccountName))
            .SingleAsync(cancellationToken);
        return new BankPaymentRequest(
            payment.Connection, trustAccount, payment.InstructionPublicId, payment.Amount, payment.Currency.Trim(),
            payment.Reference, payee);
    }
}
