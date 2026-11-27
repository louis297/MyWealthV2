using MyWealthV2.Application.Valuations;
using MyWealthV2.Domain.Tax;

namespace MyWealthV2.Application.TaxYears;

/// <summary>The one place a tax year is worked out (tax-year-events §7).</summary>
public interface ICustomerTaxLedger
{
    Task<CustomerTaxLedgerRead> ReadAsync(int tenantId, int customerId, int taxYear, CancellationToken cancellationToken);
}

/// <summary>A tax year with the Accounts and the Instruments its ids name.</summary>
public sealed record CustomerTaxLedgerRead(
    CustomerTaxYear Year,
    IReadOnlyDictionary<int, Guid> AccountIds,
    IReadOnlyDictionary<int, ValuedInstrument> Instruments);

public class CustomerTaxLedger : ICustomerTaxLedger
{
    public Task<CustomerTaxLedgerRead> ReadAsync(int tenantId, int customerId, int taxYear, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
