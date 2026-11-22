using MyWealthV2.Application.Common.Interfaces;

namespace MyWealthV2.Application.Transactions;

/// <summary>Reads the legs on visible Accounts as item DTOs, each with its leg account and Customer.</summary>
internal static class TransactionLegReader
{
    public static async Task<TransactionLegs> LoadAsync(
        IApplicationDbContext db,
        IReadOnlyCollection<int> transactionIds,
        IQueryable<int> visibleAccountIds,
        CancellationToken cancellationToken)
    {
        var cashRows = await (
            from leg in db.TransactionCashLegs.AsNoTracking()
            join account in db.Accounts.AsNoTracking() on leg.AccountId equals account.Id
            join customer in db.Users.AsNoTracking() on account.CustomerId equals customer.Id
            where transactionIds.Contains(leg.TransactionId) && visibleAccountIds.Contains(leg.AccountId)
            orderby leg.Id
            select new
            {
                leg.Id,
                leg.TransactionId,
                AccountId = account.PublicId,
                CustomerId = customer.PublicId,
                leg.Role,
                leg.Amount,
                leg.Currency
            }).ToListAsync(cancellationToken);

        var feeRows = await (
            from detail in db.TransactionFeeDetails.AsNoTracking()
            join schedule in db.TradeFeeSchedules.AsNoTracking() on detail.TradeFeeScheduleId equals schedule.Id into schedules
            from schedule in schedules.DefaultIfEmpty()
            where transactionIds.Contains(detail.TransactionId)
            select new
            {
                detail.CashLegId,
                detail.Kind,
                ScheduleId = schedule == null ? (Guid?)null : schedule.PublicId,
                detail.BasisAmount,
                detail.FeeAmount,
                detail.Currency
            }).ToDictionaryAsync(row => row.CashLegId, cancellationToken);

        var taxRows = await db.TransactionTaxDetails.AsNoTracking()
            .Where(detail => transactionIds.Contains(detail.TransactionId))
            .Select(detail => new
            {
                detail.CashLegId,
                detail.Kind,
                detail.RatePercent,
                detail.BasisAmount,
                detail.TaxAmount,
                detail.Currency
            }).ToDictionaryAsync(row => row.CashLegId, cancellationToken);

        var interestRows = await db.TransactionInterestDetails.AsNoTracking()
            .Where(detail => transactionIds.Contains(detail.TransactionId))
            .ToListAsync(cancellationToken);

        var corporateActionRows = await (
            from detail in db.TransactionCorporateActionDetails.AsNoTracking()
            join transaction in db.Transactions.AsNoTracking() on detail.TransactionId equals transaction.Id
            join original in db.Transactions.AsNoTracking() on transaction.OriginalTransactionId equals original.Id into originals
            from original in originals.DefaultIfEmpty()
            where transactionIds.Contains(detail.TransactionId)
            select new
            {
                detail,
                ActionId = transaction.SourceKind == Domain.Enums.TransactionSourceKind.CorporateAction
                    ? transaction.SourcePublicId
                    : original == null ? null : original.SourcePublicId
            }).ToListAsync(cancellationToken);

        var securityRows = await (
            from leg in db.TransactionSecurityLegs.AsNoTracking()
            join account in db.Accounts.AsNoTracking() on leg.AccountId equals account.Id
            join customer in db.Users.AsNoTracking() on account.CustomerId equals customer.Id
            join instrument in db.Instruments.AsNoTracking() on leg.InstrumentId equals instrument.Id
            where transactionIds.Contains(leg.TransactionId) && visibleAccountIds.Contains(leg.AccountId)
            orderby leg.Id
            select new
            {
                leg.TransactionId,
                AccountId = account.PublicId,
                CustomerId = customer.PublicId,
                InstrumentId = instrument.PublicId,
                leg.Quantity,
                leg.CostAmount,
                leg.CostCurrency
            }).ToListAsync(cancellationToken);

        var fxRows = await db.Transactions.AsNoTracking()
            .Where(transaction => transactionIds.Contains(transaction.Id) && transaction.FxDetail != null)
            .Select(transaction => new
            {
                transaction.Id,
                transaction.FxDetail!.BaseCurrency,
                transaction.FxDetail.QuoteCurrency,
                transaction.FxDetail.Rate,
                transaction.FxDetail.RateAsOfDate
            }).ToListAsync(cancellationToken);

        var adjustmentRows = await db.Transactions.AsNoTracking()
            .Where(transaction => transactionIds.Contains(transaction.Id) && transaction.AdjustmentDetail != null)
            .Select(transaction => new { transaction.Id, transaction.AdjustmentDetail!.Reason })
            .ToListAsync(cancellationToken);

        var receiptRows = await db.Transactions.AsNoTracking()
            .Where(transaction => transactionIds.Contains(transaction.Id) && transaction.BankReceiptDetail != null)
            .Select(transaction => new
            {
                transaction.Id,
                transaction.BankReceiptDetail!.ValueDate,
                transaction.BankReceiptDetail.Reference,
                transaction.BankReceiptDetail.PayerName,
                transaction.BankReceiptDetail.PayerAccount
            }).ToListAsync(cancellationToken);

        var paymentRows = await db.Transactions.AsNoTracking()
            .Where(transaction => transactionIds.Contains(transaction.Id) && transaction.PaymentDetail != null)
            .Select(transaction => new
            {
                transaction.Id,
                transaction.PaymentDetail!.ValueDate,
                transaction.PaymentDetail.Reference,
                transaction.PaymentDetail.PayeeName,
                transaction.PaymentDetail.PayeeAccount
            }).ToListAsync(cancellationToken);

        return new TransactionLegs(
            cashRows.ToLookup(row => row.TransactionId, row => new TransactionCashDto
            {
                AccountId = row.AccountId,
                CustomerId = row.CustomerId,
                Role = row.Role.ToString(),
                Amount = row.Amount,
                Currency = row.Currency.Trim(),
                Fee = feeRows.TryGetValue(row.Id, out var fee)
                    ? new TransactionFeeDto
                    {
                        Kind = fee.Kind.ToString(),
                        ScheduleId = fee.ScheduleId,
                        BasisAmount = fee.BasisAmount,
                        Amount = fee.FeeAmount,
                        Currency = fee.Currency.Trim()
                    }
                    : null,
                Tax = taxRows.TryGetValue(row.Id, out var tax)
                    ? new TransactionTaxDto
                    {
                        Kind = tax.Kind.ToString(),
                        RatePercent = tax.RatePercent,
                        BasisAmount = tax.BasisAmount,
                        Amount = tax.TaxAmount,
                        Currency = tax.Currency.Trim()
                    }
                    : null
            }),
            securityRows.ToLookup(row => row.TransactionId, row => new TransactionSecurityDto
            {
                AccountId = row.AccountId,
                CustomerId = row.CustomerId,
                InstrumentId = row.InstrumentId,
                Quantity = row.Quantity,
                Cost = row.CostAmount,
                CostCurrency = row.CostCurrency.Trim()
            }),
            fxRows.ToDictionary(row => row.Id, row => new TransactionFxDto
            {
                BaseCurrency = row.BaseCurrency.Trim(),
                QuoteCurrency = row.QuoteCurrency.Trim(),
                Rate = row.Rate,
                RateAsOfDate = row.RateAsOfDate.ToString("yyyy-MM-dd")
            }),
            adjustmentRows.ToDictionary(
                row => row.Id,
                row => new TransactionAdjustmentDto { Reason = row.Reason.ToString() }),
            receiptRows.ToDictionary(
                row => row.Id,
                row => new TransactionBankReceiptDto
                {
                    ValueDate = row.ValueDate.ToString("yyyy-MM-dd"),
                    Reference = row.Reference,
                    PayerName = row.PayerName,
                    PayerAccount = row.PayerAccount
                }),
            paymentRows.ToDictionary(
                row => row.Id,
                row => new TransactionPaymentDto
                {
                    ValueDate = row.ValueDate.ToString("yyyy-MM-dd"),
                    Reference = row.Reference,
                    PayeeName = row.PayeeName,
                    PayeeAccount = row.PayeeAccount
                }),
            interestRows.ToDictionary(
                row => row.TransactionId,
                row => new TransactionInterestDto
                {
                    PeriodFrom = row.PeriodFrom.ToString("yyyy-MM-dd"),
                    PeriodTo = row.PeriodTo.ToString("yyyy-MM-dd"),
                    Days = row.Days,
                    AccruedAmount = row.AccruedAmount,
                    GrossAmount = row.GrossAmount
                }),
            corporateActionRows.ToDictionary(
                row => row.detail.TransactionId,
                row => new TransactionCorporateActionDto
                {
                    Id = row.ActionId ?? Guid.Empty,
                    Kind = row.detail.Kind.ToString(),
                    RatioNew = row.detail.RatioNew,
                    RatioHeld = row.detail.RatioHeld,
                    ExDate = row.detail.ExDate.ToString("yyyy-MM-dd"),
                    RecordDate = row.detail.RecordDate?.ToString("yyyy-MM-dd"),
                    PaymentDate = row.detail.PaymentDate?.ToString("yyyy-MM-dd"),
                    QuantityBefore = row.detail.QuantityBefore,
                    QuantityAfter = row.detail.QuantityAfter
                }));
    }
}

internal sealed record TransactionLegs(
    ILookup<int, TransactionCashDto> Cash,
    ILookup<int, TransactionSecurityDto> Security,
    IReadOnlyDictionary<int, TransactionFxDto> Fx,
    IReadOnlyDictionary<int, TransactionAdjustmentDto> Adjustment,
    IReadOnlyDictionary<int, TransactionBankReceiptDto> BankReceipt,
    IReadOnlyDictionary<int, TransactionPaymentDto> Payment,
    IReadOnlyDictionary<int, TransactionInterestDto> Interest,
    IReadOnlyDictionary<int, TransactionCorporateActionDto> CorporateAction);
