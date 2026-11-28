using System.Text.Json;
using System.Text.Json.Serialization;
using FluentValidation.Results;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Domain.Tax;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.Reports;

/// <summary>
/// The parameters <c>{ customerId, taxYear }</c> of a report of one Customer and one tax year, with their checks and
/// messages (docs/features/fif-report.md R6, R7; docs/features/income-summary.md R18), and the tax residency on file.
/// </summary>
public static class CustomerTaxYearParameters
{
    // Fif-report R6: taxYear is a JSON whole number. The web defaults read a number from a string, so the reading is strict here.
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        NumberHandling = JsonNumberHandling.Strict
    };

    /// <summary>
    /// Binds <paramref name="parameters"/>. <paramref name="today"/> is the platform's UTC date, for the tax years that
    /// can be reported (fif-report R7, tax-year-events R4).
    /// </summary>
    public static CustomerTaxYearBinding Bind(JsonElement parameters, DateOnly today)
    {
        Input input;
        try
        {
            input = parameters.Deserialize<Input>(Options) ?? throw Invalid();
        }
        catch (JsonException exception) when (exception.Message.Contains("could not be mapped", StringComparison.OrdinalIgnoreCase))
        {
            throw Failure("parameters", "unknown field.");
        }
        catch (JsonException)
        {
            throw Invalid();
        }

        if (input.CustomerId is null || input.CustomerId == Guid.Empty)
            throw Failure("customerId", "customerId is required.");

        if (input.TaxYear is not { } taxYear)
            throw Failure("taxYear", "taxYear is required.");

        if (taxYear < TaxYear.FirstReadable)
            throw Failure("taxYear", $"taxYear must be {TaxYear.FirstReadable} or later.");

        if (!TaxYear.IsReadable(taxYear, today))
            throw Failure("taxYear", $"Tax year {taxYear} can be reported from 2 April {taxYear}.");

        var snapshot = JsonSerializer.Serialize(new Snapshot(input.CustomerId.Value, taxYear), Options);
        return new CustomerTaxYearBinding(input.CustomerId.Value, taxYear, snapshot);
    }

    /// <summary>
    /// The tax residency on file for the Customer (interest-run R2); a Customer with no saved profile reads as
    /// <see cref="TaxResidency.NewZealand"/>. Names the Tenant itself, so a <c>worker</c> run reads it the same way.
    /// </summary>
    public static async Task<TaxResidency> ResidencyAsync(
        IApplicationDbContext db,
        int tenantId,
        int customerId,
        CancellationToken cancellationToken) =>
        await db.CustomerTaxProfiles.IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.TenantId == tenantId && row.UserId == customerId)
            .Select(row => (TaxResidency?)row.TaxResidency)
            .SingleOrDefaultAsync(cancellationToken)
        ?? CustomerTaxProfile.NotProvided.TaxResidency;

    private static ValidationException Invalid() => Failure("parameters", "parameters are not valid.");

    private static ValidationException Failure(string key, string message) => new([new ValidationFailure(key, message)]);

    private sealed record Snapshot(Guid CustomerId, int TaxYear);

    private sealed class Input
    {
        public Guid? CustomerId { get; set; }

        public int? TaxYear { get; set; }
    }
}

public sealed record CustomerTaxYearBinding(Guid CustomerId, int TaxYear, string SnapshotJson);
