using System.Net;
using System.Text.Json;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.BankFeed;
using MyWealthV2.Application.FunctionalTests.Custody;
using MyWealthV2.Application.FunctionalTests.NominatedBankAccounts;
using MyWealthV2.Domain.Entities;

namespace MyWealthV2.Application.FunctionalTests.Interest;

/// <summary>Interest-run R1–R8: the Customer tax profile over HTTP.</summary>
[Parallelizable]
public class TaxProfileTests : TestBase
{
    [Test]
    public async Task WithoutBearer_Returns401_OnEveryRoute()
    {
        var id = Guid.NewGuid();
        (await TaxProfileHttp.Send(HttpMethod.Get, TaxProfileHttp.Path(id), null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await TaxProfileHttp.Send(HttpMethod.Put, TaxProfileHttp.Path(id), null, TaxProfileHttp.Body()))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await TaxProfileHttp.Send(HttpMethod.Get, TaxProfileHttp.MePath, null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task SystemAdmin_Returns403_OnTheCustomerRoutes_And404_OnMine()
    {
        var world = await NominatedWorld.CreateAsync();
        var system = await AccountHttp.SignInSystemAdmin();

        (await TaxProfileHttp.Send(HttpMethod.Get, TaxProfileHttp.Path(world.Customer.PublicId), system.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await TaxProfileHttp.PutAsync(system.AccessToken, world.Customer.PublicId, TaxProfileHttp.Body()))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await TaxProfileHttp.Send(HttpMethod.Get, TaxProfileHttp.MePath, system.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Customer_Returns403_OnTheCustomerRoutes()
    {
        var world = await NominatedWorld.CreateAsync();
        var token = world.CustomerToken.AccessToken;

        (await TaxProfileHttp.Send(HttpMethod.Get, TaxProfileHttp.Path(world.Customer.PublicId), token))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await TaxProfileHttp.PutAsync(token, world.Customer.PublicId, TaxProfileHttp.Body()))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task DisabledTenant_Returns403()
    {
        var world = await NominatedWorld.CreateAsync();
        await TenantDisabler.DisableAsync(world.Tenant.Id);

        (await TaxProfileHttp.Send(HttpMethod.Get, TaxProfileHttp.Path(world.Customer.PublicId), world.TenantAdmin.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task NeverSaved_ReadsAsNotProvided_WithholdingAt45()
    {
        var world = await NominatedWorld.CreateAsync();

        var item = await TaxProfileHttp.GetAsync(world.AdviserToken.AccessToken, world.Customer.PublicId);

        item.GetProperty("taxResidency").GetString().ShouldBe("NewZealand");
        item.GetProperty("irdNumberHeld").GetBoolean().ShouldBeFalse();
        item.GetProperty("rwtRatePercent").ValueKind.ShouldBe(JsonValueKind.Null);
        Withholding(item).ShouldBe(("Rwt", 45m, "NonDeclaration"));
        item.GetProperty("updatedBy").ValueKind.ShouldBe(JsonValueKind.Null);
        item.GetProperty("updatedAt").ValueKind.ShouldBe(JsonValueKind.Null);
        item.GetProperty("rowVersion").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public async Task TenantAdmin_SetsTheProfile_WithTheFirstWrite_AndReadsItBack()
    {
        var world = await NominatedWorld.CreateAsync();
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        var response = await TaxProfileHttp.PutAsync(world.TenantAdmin.AccessToken, world.Customer.PublicId, TaxProfileHttp.Body());

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        var item = await TaxProfileHttp.GetAsync(world.TenantAdmin.AccessToken, world.Customer.PublicId);
        item.GetProperty("taxResidency").GetString().ShouldBe("NewZealand");
        item.GetProperty("irdNumberHeld").GetBoolean().ShouldBeTrue();
        item.GetProperty("rwtRatePercent").GetDecimal().ShouldBe(17.5m);
        Withholding(item).ShouldBe(("Rwt", 17.5m, "Elected"));
        var updatedBy = item.GetProperty("updatedBy");
        updatedBy.GetProperty("name").GetString().ShouldBe("Tess Admin");
        updatedBy.GetProperty("id").GetGuid().ShouldNotBe(Guid.Empty);
        item.GetProperty("updatedAt").GetDateTimeOffset().ShouldBeGreaterThan(before);
        item.GetProperty("rowVersion").GetString().ShouldNotBeNullOrEmpty();
    }

    [Test]
    public async Task AssignedAdviser_SetsAndChangesTheProfile_ToEachWithholdingRow()
    {
        var world = await NominatedWorld.CreateAsync();
        var token = world.AdviserToken.AccessToken;

        await TaxProfileHttp.SetAsync(token, world.Customer.PublicId, "NewZealand", true, null);
        var item = await TaxProfileHttp.GetAsync(token, world.Customer.PublicId);
        Withholding(item).ShouldBe(("Rwt", 33m, "Default"));
        item.GetProperty("updatedBy").GetProperty("id").GetGuid().ShouldBe(world.Adviser.PublicId);

        await TaxProfileHttp.SetAsync(token, world.Customer.PublicId, "NewZealand", false, null);
        Withholding(await TaxProfileHttp.GetAsync(token, world.Customer.PublicId)).ShouldBe(("Rwt", 45m, "NonDeclaration"));

        await TaxProfileHttp.SetAsync(token, world.Customer.PublicId, "Overseas", true, null);
        item = await TaxProfileHttp.GetAsync(token, world.Customer.PublicId);
        Withholding(item).ShouldBe(("Nrwt", 15m, "NonResident"));
        item.GetProperty("irdNumberHeld").GetBoolean().ShouldBeTrue();
    }

    [Test]
    public async Task AnotherAdvisersCustomer_AndAnIdThatIsNotACustomer_Return404()
    {
        var world = await NominatedWorld.CreateAsync();
        var token = world.AdviserToken.AccessToken;

        foreach (var id in new[] { world.OtherCustomer.PublicId, world.Adviser.PublicId, Guid.NewGuid() })
        {
            (await TaxProfileHttp.Send(HttpMethod.Get, TaxProfileHttp.Path(id), token)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await TaxProfileHttp.PutAsync(token, id, TaxProfileHttp.Body())).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }
    }

    [TestCase("NewZealand", false, 17.5, "A rate can be chosen only when the IRD number is held.")]
    [TestCase("Overseas", true, 17.5, "An overseas customer has no RWT rate.")]
    [TestCase("NewZealand", true, 45, "RWT rate must be 10.5, 17.5, 30, 33 or 39.")]
    [TestCase("NewZealand", true, 20, "RWT rate must be 10.5, 17.5, 30, 33 or 39.")]
    public async Task ARefusedRate_Returns400_AtRwtRatePercent_AndStoresNothing(
        string residency, bool irdNumberHeld, decimal rate, string message)
    {
        var world = await NominatedWorld.CreateAsync();

        var response = await TaxProfileHttp.PutAsync(
            world.TenantAdmin.AccessToken, world.Customer.PublicId, TaxProfileHttp.Body(residency, irdNumberHeld, rate));

        (await CustodialHttp.ErrorsAsync(response))["RwtRatePercent"].ShouldBe([message]);
        (await TaxProfileHttp.GetAsync(world.TenantAdmin.AccessToken, world.Customer.PublicId))
            .GetProperty("rowVersion").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public async Task AMissingOrUnknownAttribute_Returns400_AtItsField()
    {
        var world = await NominatedWorld.CreateAsync();
        var token = world.TenantAdmin.AccessToken;
        var id = world.Customer.PublicId;

        var noResidency = TaxProfileHttp.Body();
        noResidency.Remove("taxResidency");
        (await CustodialHttp.ErrorsAsync(await TaxProfileHttp.PutAsync(token, id, noResidency))).Keys.ShouldContain("TaxResidency");

        var unknown = TaxProfileHttp.Body(taxResidency: "Australia");
        (await CustodialHttp.ErrorsAsync(await TaxProfileHttp.PutAsync(token, id, unknown))).Keys.ShouldContain("TaxResidency");

        var noIrd = TaxProfileHttp.Body();
        noIrd.Remove("irdNumberHeld");
        (await CustodialHttp.ErrorsAsync(await TaxProfileHttp.PutAsync(token, id, noIrd))).Keys.ShouldContain("IrdNumberHeld");

        var noRate = TaxProfileHttp.Body();
        noRate.Remove("rwtRatePercent");
        (await CustodialHttp.ErrorsAsync(await TaxProfileHttp.PutAsync(token, id, noRate))).Keys.ShouldContain("RwtRatePercent");
    }

    [TestCase("tenantId")]
    [TestCase("customerId")]
    [TestCase("withholding")]
    public async Task ABodyWithAFieldTheRouteOwns_Returns400(string field)
    {
        var world = await NominatedWorld.CreateAsync();
        var body = TaxProfileHttp.Body();
        body[field] = field == "withholding" ? new { kind = "Rwt", ratePercent = 10.5m, basis = "Elected" } : Guid.NewGuid();

        var response = await TaxProfileHttp.PutAsync(world.TenantAdmin.AccessToken, world.Customer.PublicId, body);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
        (await TaxProfileHttp.GetAsync(world.TenantAdmin.AccessToken, world.Customer.PublicId))
            .GetProperty("rowVersion").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public async Task AStaleRowVersion_AndASecondFirstWrite_Return409()
    {
        var world = await NominatedWorld.CreateAsync();
        var token = world.TenantAdmin.AccessToken;
        var id = world.Customer.PublicId;
        await TaxProfileHttp.SetAsync(token, id);

        var stale = await TaxProfileHttp.PutAsync(token, id, TaxProfileHttp.Body(rowVersion: TaxProfileHttp.StaleRowVersion));
        var secondFirst = await TaxProfileHttp.PutAsync(token, id, TaxProfileHttp.Body(rwtRatePercent: 30m));

        foreach (var response in new[] { stale, secondFirst })
        {
            response.StatusCode.ShouldBe(HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync());
            (await ProblemResponse.ReadAsync(response)).Code.ShouldBe("stale_row_version");
        }

        (await TaxProfileHttp.GetAsync(token, id)).GetProperty("rwtRatePercent").GetDecimal().ShouldBe(17.5m);
    }

    [Test]
    public async Task ADisabledCustomersProfile_MayBeChanged()
    {
        var world = await NominatedWorld.CreateAsync();
        await BankFeedHttp.SetCustomerDisabledAsync(world.Customer.Id, disabled: true);

        await TaxProfileHttp.SetAsync(world.TenantAdmin.AccessToken, world.Customer.PublicId, "Overseas", false, null);

        Withholding(await TaxProfileHttp.GetAsync(world.TenantAdmin.AccessToken, world.Customer.PublicId))
            .ShouldBe(("Nrwt", 15m, "NonResident"));
    }

    [Test]
    public async Task Mine_IsTheCustomersOwn_WithoutUpdatedBy_AndOtherRolesGet404()
    {
        var world = await NominatedWorld.CreateAsync();
        Withholding(await TaxProfileHttp.GetMineAsync(world.CustomerToken.AccessToken)).ShouldBe(("Rwt", 45m, "NonDeclaration"));
        await TaxProfileHttp.SetAsync(world.AdviserToken.AccessToken, world.Customer.PublicId, "NewZealand", true, 10.5m);

        var mine = await TaxProfileHttp.GetMineAsync(world.CustomerToken.AccessToken);

        Withholding(mine).ShouldBe(("Rwt", 10.5m, "Elected"));
        mine.GetProperty("updatedBy").ValueKind.ShouldBe(JsonValueKind.Null);
        mine.GetProperty("updatedAt").ValueKind.ShouldNotBe(JsonValueKind.Null);
        mine.GetProperty("rowVersion").ValueKind.ShouldNotBe(JsonValueKind.Null);
        Withholding(await TaxProfileHttp.GetMineAsync(world.OtherCustomerToken.AccessToken)).ShouldBe(("Rwt", 45m, "NonDeclaration"));
        (await TaxProfileHttp.Send(HttpMethod.Get, TaxProfileHttp.MePath, world.AdviserToken.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await TaxProfileHttp.Send(HttpMethod.Get, TaxProfileHttp.MePath, world.TenantAdmin.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task TwoTenants_TheOtherTenantsCustomer_Returns404_OnGetAndPut()
    {
        var world = await NominatedWorld.CreateAsync();
        var south = await NominatedWorld.CreateAsync("South Advisory", "south-advisory");
        await TaxProfileHttp.SetAsync(south.TenantAdmin.AccessToken, south.Customer.PublicId);

        (await TaxProfileHttp.Send(HttpMethod.Get, TaxProfileHttp.Path(south.Customer.PublicId), world.TenantAdmin.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await TaxProfileHttp.PutAsync(world.TenantAdmin.AccessToken, south.Customer.PublicId, TaxProfileHttp.Body(rwtRatePercent: 39m)))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await TaxProfileHttp.GetAsync(south.TenantAdmin.AccessToken, south.Customer.PublicId))
            .GetProperty("rwtRatePercent").GetDecimal().ShouldBe(17.5m);
    }

    private static (string? Kind, decimal Rate, string? Basis) Withholding(JsonElement item)
    {
        var withholding = item.GetProperty("withholding");
        return (withholding.GetProperty("kind").GetString(), withholding.GetProperty("ratePercent").GetDecimal(),
            withholding.GetProperty("basis").GetString());
    }
}
