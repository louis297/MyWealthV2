using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;
using MyWealthV2.Infrastructure.Identity;

namespace MyWealthV2.Application.FunctionalTests.Identity;

/// <summary>identity-auth amendment 2026-11-13 (lockout): five wrong passwords lock the identity user for five minutes (A41–A46).</summary>
public class HostedLoginLockoutTests : TestBase
{
    private const string Password = "Password1!";
    private const string WrongPassword = "WrongPass1!";

    [Test]
    public async Task FiveWrongPasswords_ThenCorrectPassword_IssuesNoCode()
    {
        var adviser = await CreateAdviserAsync("firml", "lee@firml");

        await FailTimesAsync(5, "lee@firml", "firml");
        var login = await FunctionalTestSetup.Oidc.SubmitLoginAsync("lee@firml", Password, "firml");

        AssertUniformFailure(login);
        var state = await IdentityStateAsync(adviser.IdentityUserId);
        state.LockoutEnd.ShouldNotBeNull();
        state.LockoutEnd.Value.ShouldBeInRange(
            DateTimeOffset.UtcNow.AddMinutes(4),
            DateTimeOffset.UtcNow.AddMinutes(6));
    }

    [Test]
    public async Task LockedAdviser_CorrectPassword_IssuesNoCode_AndLeavesLockoutUnchanged()
    {
        var adviser = await CreateAdviserAsync("firml", "lee@firml");
        await FailTimesAsync(5, "lee@firml", "firml");
        var before = await IdentityStateAsync(adviser.IdentityUserId);
        before.LockoutEnd.ShouldNotBeNull();

        var login = await FunctionalTestSetup.Oidc.SubmitLoginAsync("lee@firml", Password, "firml");

        AssertUniformFailure(login);
        (await IdentityStateAsync(adviser.IdentityUserId)).ShouldBe(before);
    }

    [Test]
    public async Task LockedAdviser_CorrectPassword_VerifiesOneHash()
    {
        var adviser = await CreateAdviserAsync("firml", "lee@firml");
        await SetLockoutEndAsync(adviser.IdentityUserId, DateTimeOffset.UtcNow.AddMinutes(5));
        var counter = FunctionalTestSetup.Identity.Services.GetRequiredService<PasswordVerificationCounter>();
        counter.Reset();

        var login = await FunctionalTestSetup.Oidc.SubmitLoginAsync("lee@firml", Password, "firml");

        AssertUniformFailure(login);
        counter.Count.ShouldBe(1);
    }

    [Test]
    public async Task ExpiredLockout_CorrectPassword_IssuesCode_AndResetsTheCount()
    {
        var adviser = await CreateAdviserAsync("firml", "lee@firml");
        await FailTimesAsync(5, "lee@firml", "firml");
        (await IdentityStateAsync(adviser.IdentityUserId)).LockoutEnd.ShouldNotBeNull();
        await SetLockoutEndAsync(adviser.IdentityUserId, DateTimeOffset.UtcNow.AddMinutes(-1));

        var login = await FunctionalTestSetup.Oidc.SubmitLoginAsync("lee@firml", Password, "firml");

        login.Code.ShouldNotBeNullOrWhiteSpace();
        (await IdentityStateAsync(adviser.IdentityUserId)).AccessFailedCount.ShouldBe(0);
    }

    [Test]
    public async Task CorrectPassword_ResetsTheCount_SoFailuresMustBeInARow()
    {
        var adviser = await CreateAdviserAsync("firml", "lee@firml");

        await FailTimesAsync(4, "lee@firml", "firml");
        (await IdentityStateAsync(adviser.IdentityUserId)).AccessFailedCount.ShouldBe(4);

        (await FunctionalTestSetup.Oidc.SubmitLoginAsync("lee@firml", Password, "firml")).Code.ShouldNotBeNullOrWhiteSpace();
        (await IdentityStateAsync(adviser.IdentityUserId)).AccessFailedCount.ShouldBe(0);

        await FailTimesAsync(4, "lee@firml", "firml");
        var state = await IdentityStateAsync(adviser.IdentityUserId);
        state.AccessFailedCount.ShouldBe(4);
        state.LockoutEnd.ShouldBeNull();

        (await FunctionalTestSetup.Oidc.SubmitLoginAsync("lee@firml", Password, "firml")).Code.ShouldNotBeNullOrWhiteSpace();
    }

    [Test]
    public async Task FailuresWithTenantB_DoNotLockTheSameEmailInTenantA()
    {
        var personA = await CreateAdviserAsync("firma", "same@shared");
        var personB = await CreateAdviserAsync("firmb", "same@shared");

        await FailTimesAsync(5, "same@shared", "firmb");

        (await IdentityStateAsync(personB.IdentityUserId)).LockoutEnd.ShouldNotBeNull();
        (await IdentityStateAsync(personA.IdentityUserId)).ShouldBe((0, (DateTimeOffset?)null));
        (await FunctionalTestSetup.Oidc.SubmitLoginAsync("same@shared", Password, "firma")).Code.ShouldNotBeNullOrWhiteSpace();
    }

    [Test]
    public async Task SystemAdmin_FiveWrongPasswords_OnBackOffice_IsLockedToo()
    {
        var email = DevelopmentIdentitySeeder.SystemAdminEmail;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            AssertUniformFailure(await FunctionalTestSetup.BackOfficeOidc.SubmitLoginAsync(email, WrongPassword, tenantCode: null));
        }

        var login = await FunctionalTestSetup.BackOfficeOidc.SubmitLoginAsync(
            email, DevelopmentIdentitySeeder.SystemAdminPassword, tenantCode: null);

        AssertUniformFailure(login);
        (await IdentityStateAsync(await SystemAdminIdentityUserIdAsync(email))).LockoutEnd.ShouldNotBeNull();
    }

    private static async Task<MyWealthV2.Domain.Entities.User> CreateAdviserAsync(string tenantCode, string email)
    {
        var tenant = await TestApp.CreateTenantAsync("Firm " + tenantCode, tenantCode);
        return await TestApp.CreatePersonAsync(UserRole.Adviser, "Lee", email, Password, tenant.Id);
    }

    private static async Task FailTimesAsync(int times, string email, string tenantCode)
    {
        for (var attempt = 0; attempt < times; attempt++)
        {
            AssertUniformFailure(await FunctionalTestSetup.Oidc.SubmitLoginAsync(email, WrongPassword, tenantCode));
        }
    }

    private static void AssertUniformFailure(HostedLoginResult login)
    {
        login.Code.ShouldBeNull();
        var copy = Regex.Match(login.Body, @"<p>([^<]+)</p>");
        copy.Success.ShouldBeTrue();
        copy.Groups[1].Value.ShouldBe("Invalid login.");
    }

    private static async Task<(int AccessFailedCount, DateTimeOffset? LockoutEnd)> IdentityStateAsync(string identityUserId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = await db.Users.AsNoTracking().SingleAsync(user => user.Id == identityUserId);
        return (row.AccessFailedCount, row.LockoutEnd);
    }

    private static async Task SetLockoutEndAsync(string identityUserId, DateTimeOffset lockoutEnd)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = await db.Users.SingleAsync(user => user.Id == identityUserId);
        row.LockoutEnd = lockoutEnd;
        await db.SaveChangesAsync();
    }

    private static async Task<string> SystemAdminIdentityUserIdAsync(string email)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var person = await db.DomainUsers.SingleAsync(row => row.Role == UserRole.SystemAdmin && row.Email == email);
        return person.IdentityUserId;
    }
}
