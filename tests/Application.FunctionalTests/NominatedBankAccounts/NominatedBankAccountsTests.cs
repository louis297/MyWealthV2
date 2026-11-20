using System.Net;
using System.Text.Json;
using MyWealthV2.Application.FunctionalTests.Accounts;
using MyWealthV2.Application.FunctionalTests.BankFeed;
using MyWealthV2.Application.FunctionalTests.Custody;

namespace MyWealthV2.Application.FunctionalTests.NominatedBankAccounts;

/// <summary>Withdrawals R1–R16: nominated bank accounts over HTTP.</summary>
[Parallelizable]
public class NominatedBankAccountsTests : TestBase
{
    private const string FourEyesMessage = "A bank account must be verified by someone other than the person who added it.";

    [Test]
    public async Task WithoutBearer_Returns401_OnEveryRoute()
    {
        foreach (var (method, route, body) in NominatedBankAccountHttp.Routes(Guid.NewGuid(), Guid.NewGuid()))
        {
            (await NominatedBankAccountHttp.Send(method, route, accessToken: null, body))
                .StatusCode.ShouldBe(HttpStatusCode.Unauthorized, $"{method} {route}");
        }
    }

    [Test]
    public async Task SystemAdmin_Returns403_OnEveryRoute()
    {
        var world = await NominatedWorld.CreateAsync();
        var id = await NominatedBankAccountHttp.AddAsync(world.AdviserToken.AccessToken, world.Customer.PublicId);
        var system = await AccountHttp.SignInSystemAdmin();

        foreach (var (method, route, body) in NominatedBankAccountHttp.Routes(id, world.Customer.PublicId))
        {
            (await NominatedBankAccountHttp.Send(method, route, system.AccessToken, body))
                .StatusCode.ShouldBe(HttpStatusCode.Forbidden, $"{method} {route}");
        }
    }

    [Test]
    public async Task Customer_Returns403_OnEveryWrite_AndReadsOnlyTheirOwn()
    {
        var world = await NominatedWorld.CreateAsync();
        var own = await NominatedBankAccountHttp.AddAsync(world.AdviserToken.AccessToken, world.Customer.PublicId);
        var other = await NominatedBankAccountHttp.AddAsync(world.TenantAdmin.AccessToken, world.OtherCustomer.PublicId);
        var token = world.CustomerToken.AccessToken;

        foreach (var (method, route, body) in NominatedBankAccountHttp.Routes(own, world.Customer.PublicId).Where(r => r.Method == HttpMethod.Post))
        {
            (await NominatedBankAccountHttp.Send(method, route, token, body))
                .StatusCode.ShouldBe(HttpStatusCode.Forbidden, $"{method} {route}");
        }

        (await NominatedBankAccountHttp.GetAsync(token, own)).GetProperty("id").GetGuid().ShouldBe(own);
        (await NominatedBankAccountHttp.ListIdsAsync(token)).ShouldBe([own]);
        (await NominatedBankAccountHttp.ListIdsAsync(token, $"?customerId={world.OtherCustomer.PublicId}")).ShouldBeEmpty();
        (await NominatedBankAccountHttp.Send(HttpMethod.Get, $"{NominatedBankAccountHttp.Path}/{other}", token))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task DisabledTenant_Returns403()
    {
        var world = await NominatedWorld.CreateAsync();
        await TenantDisabler.DisableAsync(world.Tenant.Id);

        (await NominatedBankAccountHttp.Send(HttpMethod.Get, NominatedBankAccountHttp.Path, world.TenantAdmin.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task Add_ByAnAdviser_Returns201_Pending_WithTheFullNumberAndWhoAddedIt()
    {
        var world = await NominatedWorld.CreateAsync();
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        var response = await NominatedBankAccountHttp.PostAsync(world.AdviserToken.AccessToken, NominatedBankAccountHttp.Body(
            world.Customer.PublicId, " usd ", " 06-0123-0456789-00 ", " ANZ ", " J Smith "));

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var id = await CustodialHttp.ReadIdAsync(response);
        var item = await NominatedBankAccountHttp.GetAsync(world.TenantAdmin.AccessToken, id);
        item.GetProperty("customerId").GetGuid().ShouldBe(world.Customer.PublicId);
        item.GetProperty("currency").GetString().ShouldBe("USD");
        item.GetProperty("bankName").GetString().ShouldBe("ANZ");
        item.GetProperty("accountName").GetString().ShouldBe("J Smith");
        item.GetProperty("accountNumber").GetString().ShouldBe("06-0123-0456789-00");
        item.GetProperty("status").GetString().ShouldBe("Pending");
        item.GetProperty("added").GetProperty("by").GetProperty("id").GetGuid().ShouldBe(world.Adviser.PublicId);
        item.GetProperty("added").GetProperty("by").GetProperty("name").GetString().ShouldBe("Ann Adviser");
        item.GetProperty("added").GetProperty("at").GetDateTimeOffset().ShouldBeGreaterThan(before);
        item.GetProperty("review").ValueKind.ShouldBe(JsonValueKind.Null);
        item.GetProperty("removal").ValueKind.ShouldBe(JsonValueKind.Null);
        item.GetProperty("rowVersion").GetString().ShouldNotBeNullOrEmpty();
    }

    [TestCase("bankName", "", "BankName")]
    [TestCase("bankName", "101", "BankName")]
    [TestCase("accountName", " ", "AccountName")]
    [TestCase("accountName", "141", "AccountName")]
    [TestCase("accountNumber", "12  34", "AccountNumber")]
    [TestCase("accountNumber", "12_34", "AccountNumber")]
    [TestCase("accountNumber", "35", "AccountNumber")]
    [TestCase("currency", "XXX", "Currency")]
    [TestCase("currency", "", "Currency")]
    public async Task Add_WithABadField_Returns400AtThatField(string field, string value, string key)
    {
        var world = await NominatedWorld.CreateAsync();
        var body = NominatedBankAccountHttp.Body(world.Customer.PublicId);
        body[field] = value switch
        {
            "101" => new string('B', 101),
            "141" => new string('N', 141),
            "35" => new string('1', 35),
            _ => value
        };

        var response = await NominatedBankAccountHttp.PostAsync(world.AdviserToken.AccessToken, body);

        (await CustodialHttp.ErrorsAsync(response)).ShouldContainKey(key);
    }

    [TestCase("status", "Verified")]
    [TestCase("tenantId", "a1b2c3d4-0000-0000-0000-000000000001")]
    [TestCase("id", "a1b2c3d4-0000-0000-0000-000000000002")]
    public async Task Add_WithAServerField_Returns400(string field, string value)
    {
        var world = await NominatedWorld.CreateAsync();
        var body = NominatedBankAccountHttp.Body(world.Customer.PublicId);
        body[field] = value;

        (await NominatedBankAccountHttp.PostAsync(world.TenantAdmin.AccessToken, body))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await NominatedBankAccountHttp.ListIdsAsync(world.TenantAdmin.AccessToken)).ShouldBeEmpty();
    }

    [Test]
    public async Task Add_ForADisabledCustomer_Returns400DisabledUser()
    {
        var world = await NominatedWorld.CreateAsync();
        await BankFeedHttp.SetCustomerDisabledAsync(world.Customer.Id, disabled: true);

        var response = await NominatedBankAccountHttp.PostAsync(
            world.TenantAdmin.AccessToken, NominatedBankAccountHttp.Body(world.Customer.PublicId));

        await ShouldBeDisabledUserAsync(response);
    }

    [Test]
    public async Task Add_ForAnUnassignedOrUnknownCustomer_OrAnotherTenantsCustomer_Returns404()
    {
        var world = await NominatedWorld.CreateAsync();
        var south = await NominatedWorld.CreateAsync("South Advisory", "south-advisory");

        foreach (var customerId in new[] { world.OtherCustomer.PublicId, Guid.NewGuid(), south.Customer.PublicId, world.Adviser.PublicId })
        {
            (await NominatedBankAccountHttp.PostAsync(world.AdviserToken.AccessToken, NominatedBankAccountHttp.Body(customerId)))
                .StatusCode.ShouldBe(HttpStatusCode.NotFound, customerId.ToString());
        }

        (await NominatedBankAccountHttp.PostAsync(world.TenantAdmin.AccessToken, NominatedBankAccountHttp.Body(south.Customer.PublicId)))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Add_ADuplicate_IgnoringCaseSpacesAndHyphens_Returns400AtAccountNumber()
    {
        var world = await NominatedWorld.CreateAsync();
        await NominatedBankAccountHttp.AddAsync(world.AdviserToken.AccessToken, world.Customer.PublicId, accountNumber: "gb29-nwbk 6016");

        var response = await NominatedBankAccountHttp.PostAsync(world.TenantAdmin.AccessToken,
            NominatedBankAccountHttp.Body(world.Customer.PublicId, accountNumber: "GB29NWBK-6016"));

        (await CustodialHttp.ErrorsAsync(response))["AccountNumber"]
            .ShouldBe(["This bank account is already nominated for this customer."]);
    }

    [Test]
    public async Task Add_TheSameNumber_AfterRemove_InAnotherCurrency_OrForAnotherCustomer_Returns201()
    {
        var world = await NominatedWorld.CreateAsync();
        var first = await NominatedBankAccountHttp.AddAsync(world.AdviserToken.AccessToken, world.Customer.PublicId);
        await NominatedBankAccountHttp.AddAsync(world.AdviserToken.AccessToken, world.Customer.PublicId, currency: "NZD");
        await NominatedBankAccountHttp.AddAsync(world.TenantAdmin.AccessToken, world.OtherCustomer.PublicId);
        (await NominatedBankAccountHttp.ActAsync(world.AdviserToken.AccessToken, first, "remove", "Typo"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await NominatedBankAccountHttp.AddAsync(world.AdviserToken.AccessToken, world.Customer.PublicId);
    }

    [Test]
    public async Task Verify_ByATenantAdminWhoDidNotAddIt_Returns204_AndRecordsTheReview()
    {
        var world = await NominatedWorld.CreateAsync();
        var id = await NominatedBankAccountHttp.AddAsync(world.AdviserToken.AccessToken, world.Customer.PublicId);
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        var response = await NominatedBankAccountHttp.ActAsync(world.TenantAdmin.AccessToken, id, "verify", "  Bank statement sighted 2026-11-20 ");

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        var item = await NominatedBankAccountHttp.GetAsync(world.TenantAdmin.AccessToken, id);
        item.GetProperty("status").GetString().ShouldBe("Verified");
        var review = item.GetProperty("review");
        review.GetProperty("by").GetProperty("name").GetString().ShouldBe("Tess Admin");
        review.GetProperty("at").GetDateTimeOffset().ShouldBeGreaterThan(before);
        review.GetProperty("note").GetString().ShouldBe("Bank statement sighted 2026-11-20");
    }

    [TestCase("verify")]
    [TestCase("reject")]
    public async Task VerifyOrReject_ByThePersonWhoAddedIt_Returns400AtStatus(string action)
    {
        var world = await NominatedWorld.CreateAsync();
        var id = await NominatedBankAccountHttp.AddAsync(world.TenantAdmin.AccessToken, world.Customer.PublicId);

        var response = await NominatedBankAccountHttp.ActAsync(world.TenantAdmin.AccessToken, id, action);

        (await CustodialHttp.ErrorsAsync(response))["Status"].ShouldBe([FourEyesMessage]);
        (await NominatedBankAccountHttp.ActAsync(world.SecondTenantAdmin.AccessToken, id, action))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [TestCase("verify")]
    [TestCase("reject")]
    public async Task VerifyOrReject_ByAnAdviser_Returns403(string action)
    {
        var world = await NominatedWorld.CreateAsync();
        var id = await NominatedBankAccountHttp.AddAsync(world.TenantAdmin.AccessToken, world.Customer.PublicId);

        (await NominatedBankAccountHttp.ActAsync(world.AdviserToken.AccessToken, id, action))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [TestCase("verify", null)]
    [TestCase("verify", "  ")]
    [TestCase("reject", null)]
    [TestCase("remove", null)]
    [TestCase("remove", "201")]
    public async Task AnActionWithoutAValidNote_Returns400AtNote(string action, string? note)
    {
        var world = await NominatedWorld.CreateAsync();
        var id = await NominatedBankAccountHttp.AddAsync(world.AdviserToken.AccessToken, world.Customer.PublicId);

        var response = await NominatedBankAccountHttp.ActAsync(
            world.TenantAdmin.AccessToken, id, action, note == "201" ? new string('n', 201) : note);

        (await CustodialHttp.ErrorsAsync(response)).ShouldContainKey("Note");
    }

    [Test]
    public async Task Verify_ForADisabledCustomer_Returns400DisabledUser_ButRejectAndRemoveWork()
    {
        var world = await NominatedWorld.CreateAsync();
        var first = await NominatedBankAccountHttp.AddAsync(world.AdviserToken.AccessToken, world.Customer.PublicId);
        var second = await NominatedBankAccountHttp.AddAsync(world.AdviserToken.AccessToken, world.Customer.PublicId, currency: "NZD");
        await BankFeedHttp.SetCustomerDisabledAsync(world.Customer.Id, disabled: true);

        await ShouldBeDisabledUserAsync(await NominatedBankAccountHttp.ActAsync(world.TenantAdmin.AccessToken, first, "verify"));
        (await NominatedBankAccountHttp.ActAsync(world.TenantAdmin.AccessToken, first, "reject", "Customer left"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await NominatedBankAccountHttp.ActAsync(world.TenantAdmin.AccessToken, second, "remove", "Customer left"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task Reject_RecordsTheReview_AndRemove_FromPendingAndVerified_RecordsTheRemoval()
    {
        var world = await NominatedWorld.CreateAsync();
        var rejected = await NominatedBankAccountHttp.AddAsync(world.AdviserToken.AccessToken, world.Customer.PublicId, accountNumber: "111");
        var pending = await NominatedBankAccountHttp.AddAsync(world.AdviserToken.AccessToken, world.Customer.PublicId, accountNumber: "222");
        var verified = await NominatedBankAccountHttp.AddAsync(world.AdviserToken.AccessToken, world.Customer.PublicId, accountNumber: "333");
        await NominatedBankAccountHttp.VerifyAsync(world.TenantAdmin.AccessToken, verified);

        (await NominatedBankAccountHttp.ActAsync(world.TenantAdmin.AccessToken, rejected, "reject", "Name differs"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await NominatedBankAccountHttp.ActAsync(world.AdviserToken.AccessToken, pending, "remove", "Typo"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await NominatedBankAccountHttp.ActAsync(world.AdviserToken.AccessToken, verified, "remove", "Closed"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var r = await NominatedBankAccountHttp.GetAsync(world.TenantAdmin.AccessToken, rejected);
        r.GetProperty("status").GetString().ShouldBe("Rejected");
        r.GetProperty("review").GetProperty("note").GetString().ShouldBe("Name differs");
        var p = await NominatedBankAccountHttp.GetAsync(world.TenantAdmin.AccessToken, pending);
        p.GetProperty("status").GetString().ShouldBe("Removed");
        p.GetProperty("review").ValueKind.ShouldBe(JsonValueKind.Null);
        p.GetProperty("removal").GetProperty("by").GetProperty("id").GetGuid().ShouldBe(world.Adviser.PublicId);
        p.GetProperty("removal").GetProperty("note").GetString().ShouldBe("Typo");
        var v = await NominatedBankAccountHttp.GetAsync(world.TenantAdmin.AccessToken, verified);
        v.GetProperty("status").GetString().ShouldBe("Removed");
        v.GetProperty("review").GetProperty("note").GetString().ShouldBe("Bank statement sighted");
        v.GetProperty("removal").GetProperty("note").GetString().ShouldBe("Closed");
    }

    [Test]
    public async Task AnActionFromAWrongStatus_Returns400AtStatus()
    {
        var world = await NominatedWorld.CreateAsync();
        var id = await NominatedBankAccountHttp.AddAsync(world.AdviserToken.AccessToken, world.Customer.PublicId);
        await NominatedBankAccountHttp.VerifyAsync(world.TenantAdmin.AccessToken, id);

        (await CustodialHttp.ErrorsAsync(await NominatedBankAccountHttp.ActAsync(world.SecondTenantAdmin.AccessToken, id, "verify")))["Status"]
            .ShouldBe(["This bank account is Verified and cannot be verified."]);
        (await CustodialHttp.ErrorsAsync(await NominatedBankAccountHttp.ActAsync(world.SecondTenantAdmin.AccessToken, id, "reject")))["Status"]
            .ShouldBe(["This bank account is Verified and cannot be rejected."]);
        (await NominatedBankAccountHttp.ActAsync(world.AdviserToken.AccessToken, id, "remove", "Closed"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await CustodialHttp.ErrorsAsync(await NominatedBankAccountHttp.ActAsync(world.AdviserToken.AccessToken, id, "remove", "Again")))["Status"]
            .ShouldBe(["This bank account is Removed and cannot be removed."]);
    }

    [Test]
    public async Task AStaleRowVersion_Returns409_AndAMissingOne_Returns400()
    {
        var world = await NominatedWorld.CreateAsync();
        var id = await NominatedBankAccountHttp.AddAsync(world.AdviserToken.AccessToken, world.Customer.PublicId);
        var path = $"{NominatedBankAccountHttp.Path}/{id}/verify";

        var stale = await NominatedBankAccountHttp.Send(HttpMethod.Post, path, world.TenantAdmin.AccessToken,
            new { note = "Checked", rowVersion = NominatedBankAccountHttp.StaleRowVersion });
        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        using (var json = JsonDocument.Parse(await stale.Content.ReadAsStringAsync()))
        {
            json.RootElement.GetProperty("code").GetString().ShouldBe("stale_row_version");
        }

        (await CustodialHttp.ErrorsAsync(await NominatedBankAccountHttp.Send(HttpMethod.Post, path, world.TenantAdmin.AccessToken,
            new { note = "Checked" }))).ShouldContainKey("RowVersion");
        (await NominatedBankAccountHttp.GetAsync(world.TenantAdmin.AccessToken, id)).GetProperty("status").GetString().ShouldBe("Pending");
    }

    [Test]
    public async Task ACustomer_SeesTheVerifyNote_ButNotARejectOrRemoveNote()
    {
        var world = await NominatedWorld.CreateAsync();
        var verified = await NominatedBankAccountHttp.AddAsync(world.AdviserToken.AccessToken, world.Customer.PublicId, accountNumber: "111");
        var rejected = await NominatedBankAccountHttp.AddAsync(world.AdviserToken.AccessToken, world.Customer.PublicId, accountNumber: "222");
        var removed = await NominatedBankAccountHttp.AddAsync(world.AdviserToken.AccessToken, world.Customer.PublicId, accountNumber: "333");
        await NominatedBankAccountHttp.VerifyAsync(world.TenantAdmin.AccessToken, verified);
        await NominatedBankAccountHttp.ActAsync(world.TenantAdmin.AccessToken, rejected, "reject", "Suspicious");
        await NominatedBankAccountHttp.ActAsync(world.AdviserToken.AccessToken, removed, "remove", "Private reason");
        var token = world.CustomerToken.AccessToken;

        var v = await NominatedBankAccountHttp.GetAsync(token, verified);
        v.GetProperty("accountNumber").GetString().ShouldBe("111");
        v.GetProperty("review").GetProperty("note").GetString().ShouldBe("Bank statement sighted");
        v.GetProperty("review").GetProperty("by").GetProperty("name").GetString().ShouldBe("Tess Admin");
        var r = await NominatedBankAccountHttp.GetAsync(token, rejected);
        r.GetProperty("review").GetProperty("note").ValueKind.ShouldBe(JsonValueKind.Null);
        r.GetProperty("review").GetProperty("by").GetProperty("name").GetString().ShouldBe("Tess Admin");
        var m = await NominatedBankAccountHttp.GetAsync(token, removed);
        m.GetProperty("removal").GetProperty("note").ValueKind.ShouldBe(JsonValueKind.Null);
        var listed = (await NominatedBankAccountHttp.ListAsync(token, "?status=Rejected")).GetProperty("items").EnumerateArray().Single();
        listed.GetProperty("review").GetProperty("note").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public async Task List_IsNewestFirst_AndFiltersByCustomerStatusAndCurrency()
    {
        var world = await NominatedWorld.CreateAsync();
        var a = await NominatedBankAccountHttp.AddAsync(world.AdviserToken.AccessToken, world.Customer.PublicId, accountNumber: "111");
        var b = await NominatedBankAccountHttp.AddAsync(world.AdviserToken.AccessToken, world.Customer.PublicId, currency: "NZD", accountNumber: "222");
        var c = await NominatedBankAccountHttp.AddAsync(world.TenantAdmin.AccessToken, world.OtherCustomer.PublicId, accountNumber: "333");
        await NominatedBankAccountHttp.VerifyAsync(world.TenantAdmin.AccessToken, a);
        var token = world.TenantAdmin.AccessToken;

        (await NominatedBankAccountHttp.ListIdsAsync(token)).ShouldBe([c, b, a]);
        (await NominatedBankAccountHttp.ListIdsAsync(token, $"?customerId={world.Customer.PublicId}")).ShouldBe([b, a]);
        (await NominatedBankAccountHttp.ListIdsAsync(token, "?status=Pending")).ShouldBe([c, b]);
        (await NominatedBankAccountHttp.ListIdsAsync(token, "?status=verified,Rejected")).ShouldBe([a]);
        (await NominatedBankAccountHttp.ListIdsAsync(token, "?currency=nzd")).ShouldBe([b]);
        (await NominatedBankAccountHttp.ListIdsAsync(world.AdviserToken.AccessToken)).ShouldBe([b, a]);
        (await NominatedBankAccountHttp.ListAsync(token)).GetProperty("totalCount").GetInt32().ShouldBe(3);
    }

    [Test]
    public async Task List_WithAnUnknownStatus_OrATenantId_Returns400()
    {
        var world = await NominatedWorld.CreateAsync();

        (await CustodialHttp.ErrorsAsync(await NominatedBankAccountHttp.Send(
            HttpMethod.Get, $"{NominatedBankAccountHttp.Path}?status=Pending,Gone", world.TenantAdmin.AccessToken))).ShouldContainKey("Status");
        (await NominatedBankAccountHttp.Send(
            HttpMethod.Get, $"{NominatedBankAccountHttp.Path}?tenantId={world.Tenant.PublicId}", world.TenantAdmin.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task AnAdviser_GetsA404_ForAnUnassignedCustomersRow()
    {
        var world = await NominatedWorld.CreateAsync();
        var id = await NominatedBankAccountHttp.AddAsync(world.TenantAdmin.AccessToken, world.OtherCustomer.PublicId);

        (await NominatedBankAccountHttp.Send(HttpMethod.Get, $"{NominatedBankAccountHttp.Path}/{id}", world.AdviserToken.AccessToken))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await NominatedBankAccountHttp.Send(HttpMethod.Post, $"{NominatedBankAccountHttp.Path}/{id}/remove", world.AdviserToken.AccessToken,
            new { note = "Gone", rowVersion = NominatedBankAccountHttp.StaleRowVersion })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task AnotherTenantsId_Returns404_OnGetAndEveryAction()
    {
        var north = await NominatedWorld.CreateAsync();
        var south = await NominatedWorld.CreateAsync("South Advisory", "south-advisory");
        var id = await NominatedBankAccountHttp.AddAsync(south.AdviserToken.AccessToken, south.Customer.PublicId);
        var path = $"{NominatedBankAccountHttp.Path}/{id}";

        (await NominatedBankAccountHttp.Send(HttpMethod.Get, path, north.TenantAdmin.AccessToken)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        foreach (var action in new[] { "verify", "reject", "remove" })
        {
            (await NominatedBankAccountHttp.Send(HttpMethod.Post, $"{path}/{action}", north.TenantAdmin.AccessToken,
                new { note = "Note", rowVersion = NominatedBankAccountHttp.StaleRowVersion })).StatusCode.ShouldBe(HttpStatusCode.NotFound, action);
        }

        (await NominatedBankAccountHttp.ListIdsAsync(north.TenantAdmin.AccessToken)).ShouldBeEmpty();
        (await NominatedBankAccountHttp.GetAsync(south.TenantAdmin.AccessToken, id)).GetProperty("status").GetString().ShouldBe("Pending");
    }

    private static async Task ShouldBeDisabledUserAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, content);
        using var json = JsonDocument.Parse(content);
        json.RootElement.GetProperty("code").GetString().ShouldBe("disabled");
        json.RootElement.GetProperty("target").GetString().ShouldBe("user");
    }
}
