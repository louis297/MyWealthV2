using MyWealthV2.Application.CorporateActions;
using MyWealthV2.Application.FunctionalTests.BankFeed;
using MyWealthV2.Application.FunctionalTests.NominatedBankAccounts;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using static MyWealthV2.Application.FunctionalTests.CorporateActions.CorporateActionFeedHttp;
using static MyWealthV2.Application.FunctionalTests.CorporateActions.CorporateActionHttp;

namespace MyWealthV2.Application.FunctionalTests.CorporateActions;

/// <summary>
/// A Tenant whose Custodial accounts hold <c>ACME.SIM</c> (Ned 100, Olga 10, Ned's External account 50), with a USD
/// client money account, and a feed dividend of <c>ACME.SIM</c> that went ex (dividends §10).
/// </summary>
internal sealed record DividendWorld(
    NominatedWorld World,
    Holders Holders,
    ClientMoneyAccount ClientMoney,
    Guid DividendId,
    string PartyActionId)
{
    public string TenantAdmin => World.TenantAdmin.AccessToken;

    public static CorporateActionFeedItem Item(
        string actionId = "w-CA3",
        int version = 1,
        PartyActionStatus status = PartyActionStatus.Announced,
        decimal amount = 1.00m,
        DividendTaxSource taxSource = DividendTaxSource.Foreign,
        decimal credit = 0m,
        decimal rate = 15m,
        decimal? price = null) =>
        new($"{actionId}-V{version}", actionId, version, status, "ACME.SIM", "USD", CorporateActionKind.Dividend,
            null, null, Today, null, Today.AddDays(4), amount, taxSource, credit, rate, price);

    /// <summary>The world, with the dividend announced and gone ex unless <paramref name="goEx"/> is false.</summary>
    public static async Task<DividendWorld> CreateAsync(
        decimal amount = 1.00m,
        DividendTaxSource taxSource = DividendTaxSource.Foreign,
        decimal credit = 0m,
        decimal rate = 15m,
        decimal? price = null,
        bool goEx = true,
        NominatedWorld? world = null)
    {
        world ??= await NominatedWorld.CreateAsync();
        var holders = await HoldersAsync(world, "ACME.SIM");
        var clientMoney = await BankFeedHttp.ClientMoneyAccountAsync(world.Tenant.Id);
        await DeliverAsync(Item(amount: amount, taxSource: taxSource, credit: credit, rate: rate, price: price));
        if (goEx)
        {
            await DeliverAsync(Item(version: 2, status: PartyActionStatus.Effective, amount: amount, taxSource: taxSource,
                credit: credit, rate: rate, price: price));
        }

        var action = (await ActionsOfAsync(world.Tenant.Id)).Single(row => row.Kind == CorporateActionKind.Dividend);
        return new DividendWorld(world, holders, clientMoney, action.PublicId, "w-CA3");
    }

    /// <summary>A credit line on the client money account, pulled and processed as the bank job does.</summary>
    public async Task<BankStatementLine> ReceiveAsync(decimal amount, string? reference = null, string currency = "USD")
    {
        var account = currency == ClientMoney.Currency.Trim()
            ? ClientMoney
            : await BankFeedHttp.ClientMoneyAccountAsync(World.Tenant.Id, currency);
        var credit = BankFeedHttp.Feed.AddReceipt(
            account.AccountNumber, amount, currency, reference ?? PartyActionId, "Simulator Custody", "SIM-CUSTODY");
        await BankFeedHttp.RunAsync();
        return await BankFeedHttp.LineAsync(credit.Id!);
    }

    public async Task<CorporateAction> DividendAsync() =>
        (await ActionsOfAsync(World.Tenant.Id)).Single(row => row.PublicId == DividendId);
}
