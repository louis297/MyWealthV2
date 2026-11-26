using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.Valuations;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using static MyWealthV2.Application.FunctionalTests.Valuations.ValuationHttp;

namespace MyWealthV2.Application.FunctionalTests.NetWorth;

/// <summary>
/// The Tenant of valuation-history §1.1: A1, the NZD account of account-valuation §1.1 (Customer A, Adviser Ana), and
/// B1, a USD account of Customer B (Adviser Ben) opened on Wednesday 4 March 2026 at 10:00 UTC with 7,500.25 USD. Both
/// are booked on External accounts and made Custodial in the database, as the valuation run tests do.
/// </summary>
internal sealed record FundsWorld(ValuationWorld A, User Ben, User CustomerB, Guid B1)
{
    public static readonly DateTimeOffset A1Opened = new(2026, 2, 27, 9, 0, 0, TimeSpan.Zero);
    public static readonly DateTimeOffset B1Opened = new(2026, 3, 4, 10, 0, 0, TimeSpan.Zero);

    public Tenant Tenant => A.Tenant;
    public string AdminToken => A.Token;
    public Guid A1 => A.AccountId;

    public static DateOnly Day(int day) => ValuationWorld.Day(day);

    /// <summary>The world with A1 and B1 Custodial and nothing stored yet.</summary>
    public static async Task<FundsWorld> CreateAsync()
    {
        var a = await ValuationWorld.CreateAsync();
        var ben = await TestApp.CreatePersonAsync(UserRole.Adviser, "Ben Adviser", "ben@north.example", "Password1!", a.Tenant.Id);
        var customerB = await TestApp.CreatePersonAsync(
            UserRole.Customer, "Bea Customer", "bea@north.example", "Password1!", a.Tenant.Id, ben.Id);
        var b1 = await AccountHttp.OpenAccountAsync(a.Token, customerB.PublicId, "B1", "Brokerage", "USD");
        await PostAsync(a.Token, Cash(b1, "TransferIn", 7_500.25m, "2026-03-04T10:00:00Z"));
        await ValuationRunDb.MakeCustodialAsync(a.AccountId, A1Opened);
        await ValuationRunDb.MakeCustodialAsync(b1, B1Opened);
        return new FundsWorld(a, ben, customerB, b1);
    }

    /// <summary>10:00 on Friday 13 March: every day through Tuesday 10 stored; the manual run of 09:05 stores A1's
    /// Wednesday 11 and fails on B1's.</summary>
    public async Task AtTenAsync()
    {
        await ValuationRunDb.RunAsync(Tenant.Id, Day(10));
        await ValuationRunDb.RunFailingAsync(Tenant.Id, Day(11), B1);
    }

    /// <summary>14:00: the daily run of 13:00 has stored B1's Wednesday 11.</summary>
    public Task AtFourteenAsync() => ValuationRunDb.RunAsync(Tenant.Id, Day(11));
}
