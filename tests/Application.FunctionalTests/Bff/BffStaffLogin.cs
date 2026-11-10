using MyWealthV2.Domain.Enums;

namespace MyWealthV2.Application.FunctionalTests.Bff;

/// <summary>
/// Creates a tenant admin that may sign in to the Adviser Portal BFF. SystemAdmin is a Back Office-only role.
/// </summary>
internal static class BffStaffLogin
{
    public const string Password = "Password1!";

    public static async Task<(string Email, string TenantCode)> CreateAsync()
    {
        var code = "bff-" + Guid.NewGuid().ToString("N")[..8];
        var tenant = await TestApp.CreateTenantAsync("Firm " + code, code);
        var email = "tess@" + code;
        await TestApp.CreatePersonAsync(UserRole.TenantAdmin, "Tess", email, Password, tenant.Id);
        return (email, code);
    }
}
