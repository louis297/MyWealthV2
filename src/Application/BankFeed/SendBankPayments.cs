namespace MyWealthV2.Application.BankFeed;

/// <summary>The send step of <c>bank-statement-sync:{connection}</c> (docs/features/withdrawals.md R32–R34).</summary>
public sealed class SendBankPayments
{
    public Task RunAsync(string connection, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
