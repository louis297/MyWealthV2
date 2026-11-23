using MyWealthV2.Application.AccountClosures;
using Microsoft.Extensions.Logging;
using MyWealthV2.Application.Broker;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.BankFeed;

/// <summary>
/// The send step of <c>bank-statement-sync:{connection}</c> (docs/features/withdrawals.md R32–R34; ADR 0021 rules
/// 12, 13): every <c>Pending</c> outgoing payment of the connection whose client money account is active, oldest
/// first. Each attempt is recorded and committed before the call; each outcome is one database transaction guarded by
/// the payment's row version. A payment's failure never stops the others. A receipt return is sent the same way
/// and its outcome moves the returned line (docs/features/bank-returns.md R6).
/// </summary>
public sealed class SendBankPayments(
    IApplicationDbContext db,
    IBankPayments payments,
    IBankConnections connections,
    IBrokerConnections brokers,
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
        if (request is null)
        {
            logger.LogWarning(
                "Outgoing payment {Instruction} pays a broker with no bank account in {Currency} on this host; it waits.",
                payment.InstructionPublicId, payment.Currency);
            return;
        }

        payment.RecordAttempt(time.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);

        var result = await payments.SendAsync(request, cancellationToken);

        await db.ExecuteInTransactionAsync(async token =>
        {
            if (payment.Kind == OutgoingPaymentKind.ReceiptReturn)
            {
                await ApplyToReturnAsync(payment, result, token);
                await db.SaveChangesAsync(token);
                return;
            }

            if (payment.Kind == OutgoingPaymentKind.TradeSettlement)
            {
                await ApplyToSettlementAsync(payment, result, token);
                await db.SaveChangesAsync(token);
                return;
            }

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
                // Account closure R20: the bank refusing a closure's payment ends the closure.
                await AccountClosureCompletion.EndWithWithdrawalAsync(
                    db, withdrawal.Id, AccountClosureCancelReason.PaymentFailed, time.GetUtcNow(), token);
            }
            else
            {
                RecordFailure(payment, result);
            }

            await db.SaveChangesAsync(token);
        }, cancellationToken);
    }

    /// <summary>Bank-returns R6: accepted → the line is <c>ReturnSent</c>; rejected → back in suspense. When a
    /// recall returned the line first, the answer changes only the payment.</summary>
    private async Task ApplyToReturnAsync(OutgoingPayment payment, BankPaymentResult result, CancellationToken cancellationToken)
    {
        var line = await db.BankStatementLines.IgnoreQueryFilters()
            .SingleAsync(row => row.Id == payment.ReturnOfLineId, cancellationToken);
        var waiting = line.Status == BankStatementLineStatus.ReturnRequested;
        if (result.PaymentId is { } paymentId)
        {
            payment.Accept(paymentId, time.GetUtcNow());
            if (waiting)
            {
                line.MarkReturnSent();
            }
        }
        else if (result.RejectReason is { } reason)
        {
            payment.Reject(reason);
            if (waiting)
            {
                line.MarkReturnRejected();
            }
        }
        else
        {
            RecordFailure(payment, result);
        }
    }

    /// <summary>Trade-settlement R17: accepted leaves the settlement waiting for the statement; a rejection fails
    /// it.</summary>
    private async Task ApplyToSettlementAsync(OutgoingPayment payment, BankPaymentResult result, CancellationToken cancellationToken)
    {
        if (result.PaymentId is { } paymentId)
        {
            payment.Accept(paymentId, time.GetUtcNow());
        }
        else if (result.RejectReason is { } reason)
        {
            payment.Reject(reason);
            var settlement = await db.TradeSettlements.IgnoreQueryFilters()
                .SingleAsync(row => row.Id == payment.TradeSettlementId, cancellationToken);
            if (settlement.Status == TradeSettlementStatus.AwaitingPayment)
            {
                settlement.FailPayment(TradeSettlementFailure.PaymentRejected, reason.ToString());
            }
        }
        else
        {
            RecordFailure(payment, result);
        }
    }

    private void RecordFailure(OutgoingPayment payment, BankPaymentResult result)
    {
        payment.RecordFailure(result.Failure!.Value);
        logger.LogError(
            "Outgoing payment {Instruction} failed on connection {Connection}: {Failure}; it is sent again on the next run.",
            payment.InstructionPublicId, payment.Connection, result.Failure);
    }

    /// <summary>R58: the call is built from stored data only, so every attempt sends the same body.</summary>
    private async Task<BankPaymentRequest?> RequestAsync(OutgoingPayment payment, CancellationToken cancellationToken)
    {
        var trustAccount = await db.ClientMoneyAccounts.IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.Id == payment.ClientMoneyAccountId)
            .Select(row => row.AccountNumber)
            .SingleAsync(cancellationToken);
        if (payment.Kind == OutgoingPaymentKind.ReceiptReturn)
        {
            // Bank-returns R6: the bank pays the receipt back to its payer; the platform names the line, no payee.
            var returned = await db.BankStatementLines.IgnoreQueryFilters().AsNoTracking()
                .Where(row => row.Id == payment.ReturnOfLineId)
                .Select(row => row.OutsideId)
                .SingleAsync(cancellationToken);
            return new BankPaymentRequest(
                payment.Connection, trustAccount, payment.InstructionPublicId, payment.Amount, payment.Currency.Trim(),
                null, null, returned);
        }

        if (payment.Kind == OutgoingPaymentKind.TradeSettlement)
        {
            // Trade-settlement R17, R44: the payee is the broker's own bank account, a standing instruction from
            // configuration.
            var connection = await db.TradeSettlements.IgnoreQueryFilters().AsNoTracking()
                .Where(row => row.Id == payment.TradeSettlementId)
                .Select(row => row.Connection)
                .SingleAsync(cancellationToken);
            return brokers.BankAccount(connection, payment.Currency.Trim()) is { } broker
                ? new BankPaymentRequest(
                    payment.Connection, trustAccount, payment.InstructionPublicId, payment.Amount, payment.Currency.Trim(),
                    payment.Reference, new BankPaymentPayee(broker.AccountNumber, broker.AccountName))
                : null;
        }

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
