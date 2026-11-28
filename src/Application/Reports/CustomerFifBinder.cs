using System.Text.Json;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.Reports;

/// <summary>The parameters of the FIF report (docs/features/fif-report.md R6, R7), read by
/// <see cref="CustomerTaxYearParameters"/>.</summary>
public static class CustomerFifBinder
{
    public const string Code = "customer-fif";

    /// <summary>
    /// Binds <paramref name="parameters"/>. <paramref name="today"/> is the platform's UTC date, for the tax years that
    /// can be reported (R7, tax-year-events R4).
    /// </summary>
    public static CustomerTaxYearBinding Bind(JsonElement parameters, DateOnly today) => CustomerTaxYearParameters.Bind(parameters, today);

    /// <summary>R8: the refusal of a Customer whose tax residency is not New Zealand.</summary>
    public const string OverseasMessage = "A FIF report is made for a customer whose tax residency is New Zealand.";

    /// <summary>R8: the tax residency on file for the Customer (<see cref="CustomerTaxYearParameters.ResidencyAsync"/>).</summary>
    public static Task<TaxResidency> ResidencyAsync(
        IApplicationDbContext db,
        int tenantId,
        int customerId,
        CancellationToken cancellationToken) =>
        CustomerTaxYearParameters.ResidencyAsync(db, tenantId, customerId, cancellationToken);
}
