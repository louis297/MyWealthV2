using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using MyWealthV2.Infrastructure.Data;

namespace MyWealthV2.Application.FunctionalTests.Identity;

/// <summary>identity-auth amendment 2026-11-13 (lockout): every hosted login POST verifies exactly one hash (A41, A47).</summary>
public class HostedLoginHashTests : TestBase
{
    private const string Password = "Password1!";

    [Test]
    public async Task UnknownTenantCode_VerifiesOneHash()
    {
        await TestApp.CreateTenantAsync("Firm H", "firmh");

        var (login, hashes) = await SubmitCountingAsync("anyone@firmh", Password, "nosuchfirm");

        AssertUniformFailure(login);
        hashes.ShouldBe(1);
    }

    [Test]
    public async Task UnknownEmail_InKnownTenant_VerifiesOneHash()
    {
        await TestApp.CreateTenantAsync("Firm H", "firmh");

        var (login, hashes) = await SubmitCountingAsync("nobody@firmh", Password, "firmh");

        AssertUniformFailure(login);
        hashes.ShouldBe(1);
    }

    [Test]
    public async Task UnknownEmail_WithoutTenantCode_VerifiesOneHash()
    {
        var (login, hashes) = await SubmitCountingAsync("nobody@platform", Password, tenantCode: null);

        AssertUniformFailure(login);
        hashes.ShouldBe(1);
    }

    [Test]
    public async Task DisabledAdviser_VerifiesOneHash()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm H", "firmh");
        var adviser = await TestApp.CreatePersonAsync(UserRole.Adviser, "Hal", "hal@firmh", Password, tenant.Id);
        await DisablePersonAsync(adviser.Id);

        var (login, hashes) = await SubmitCountingAsync("hal@firmh", Password, "firmh");

        AssertUniformFailure(login);
        hashes.ShouldBe(1);
    }

    [Test]
    public async Task ActiveAdviser_OnDisabledTenant_VerifiesOneHash()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm H", "firmh");
        await TestApp.CreatePersonAsync(UserRole.Adviser, "Hal", "hal@firmh", Password, tenant.Id);
        await DisableTenantAsync(tenant.Id);

        var (login, hashes) = await SubmitCountingAsync("hal@firmh", Password, "firmh");

        AssertUniformFailure(login);
        hashes.ShouldBe(1);
    }

    [Test]
    public async Task ActiveAdviser_WrongPassword_VerifiesOneHash()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm H", "firmh");
        await TestApp.CreatePersonAsync(UserRole.Adviser, "Hal", "hal@firmh", Password, tenant.Id);

        var (login, hashes) = await SubmitCountingAsync("hal@firmh", "WrongPass1!", "firmh");

        AssertUniformFailure(login);
        hashes.ShouldBe(1);
    }

    [Test]
    public async Task Customer_CorrectPassword_OnAdviserPortal_VerifiesOneHash()
    {
        var tenant = await TestApp.CreateTenantAsync("Firm H", "firmh");
        var adviser = await TestApp.CreatePersonAsync(UserRole.Adviser, "Hal", "hal@firmh", Password, tenant.Id);
        await TestApp.CreatePersonAsync(UserRole.Customer, "Ivy", "ivy@firmh", Password, tenant.Id, adviser.Id);

        var (login, hashes) = await SubmitCountingAsync("ivy@firmh", Password, "firmh");

        AssertUniformFailure(login);
        hashes.ShouldBe(1);
    }

    [Test]
    public async Task UnknownEmail_FiveAttempts_VerifiesFiveHashes_AndWritesNoIdentityRow()
    {
        await TestApp.CreateTenantAsync("Firm H", "firmh");
        var before = await IdentityRowsAsync();
        var hashes = 0;

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var (login, count) = await SubmitCountingAsync("nobody@firmh", Password, "firmh");
            AssertUniformFailure(login);
            hashes += count;
        }

        hashes.ShouldBe(5);
        (await IdentityRowsAsync()).ShouldBe(before);
    }

    private static async Task<(HostedLoginResult Login, int Hashes)> SubmitCountingAsync(
        string email,
        string password,
        string? tenantCode)
    {
        var counter = FunctionalTestSetup.Identity.Services.GetRequiredService<PasswordVerificationCounter>();
        counter.Reset();
        var login = await FunctionalTestSetup.Oidc.SubmitLoginAsync(email, password, tenantCode);
        return (login, counter.Count);
    }

    private static void AssertUniformFailure(HostedLoginResult login)
    {
        login.Code.ShouldBeNull();
        var copy = Regex.Match(login.Body, @"<p>([^<]+)</p>");
        copy.Success.ShouldBeTrue();
        copy.Groups[1].Value.ShouldBe("Invalid login.");
    }

    private static async Task<string> IdentityRowsAsync()
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var rows = await db.Users
            .AsNoTracking()
            .OrderBy(row => row.Id)
            .Select(row => new { row.Id, row.ConcurrencyStamp, row.AccessFailedCount, row.LockoutEnd })
            .ToListAsync();
        return string.Join("|", rows.Select(row => $"{row.Id};{row.ConcurrencyStamp};{row.AccessFailedCount};{row.LockoutEnd:O}"));
    }

    private static async Task DisablePersonAsync(int personId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var person = await db.DomainUsers.SingleAsync(row => row.Id == personId);
        person.Disable();
        await db.SaveChangesAsync();
    }

    private static async Task DisableTenantAsync(int tenantId)
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var tenant = await db.Tenants.SingleAsync(row => row.Id == tenantId);
        tenant.Disable();
        await db.SaveChangesAsync();
    }
}
