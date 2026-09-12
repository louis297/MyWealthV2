using MyWealthV2.Application.Users;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Application.UnitTests.Users;

public class CurrentUserDtoTests
{
    [Test]
    public void SystemAdmin_HasNullTenantAndAdviser()
    {
        var user = User.Create(
            UserRole.SystemAdmin,
            name: "Ada",
            email: "ada@localhost",
            identityUserId: "id-1");

        var dto = CurrentUserDto.From(user, tenant: null, adviser: null);

        dto.Id.ShouldBe(user.PublicId);
        dto.Name.ShouldBe("Ada");
        dto.Email.ShouldBe("ada@localhost");
        dto.Role.ShouldBe("systemAdmin");
        dto.Status.ShouldBe("active");
        dto.TenantId.ShouldBeNull();
        dto.TenantCode.ShouldBeNull();
        dto.AdviserId.ShouldBeNull();
    }

    [Test]
    public void TenantAdmin_IncludesTenantPublicIdAndCode()
    {
        var tenant = Tenant.Create("Acme", "acme");
        tenant.Id = 10;
        var user = User.Create(
            UserRole.TenantAdmin,
            name: "Bea",
            email: "bea@acme",
            identityUserId: "id-2",
            tenantId: 10);

        var dto = CurrentUserDto.From(user, tenant, adviser: null);

        dto.Role.ShouldBe("tenantAdmin");
        dto.TenantId.ShouldBe(tenant.PublicId);
        dto.TenantCode.ShouldBe("acme");
        dto.AdviserId.ShouldBeNull();
    }

    [Test]
    public void Customer_IncludesAdviserPublicId()
    {
        var tenant = Tenant.Create("Acme", "acme");
        tenant.Id = 10;
        var adviser = User.Create(
            UserRole.Adviser,
            name: "Cara",
            email: "cara@acme",
            identityUserId: "id-3",
            tenantId: 10);
        adviser.Id = 20;
        var customer = User.Create(
            UserRole.Customer,
            name: "Dee",
            email: "dee@acme",
            identityUserId: "id-4",
            tenantId: 10,
            adviserId: 20);

        var dto = CurrentUserDto.From(customer, tenant, adviser);

        dto.Role.ShouldBe("customer");
        dto.AdviserId.ShouldBe(adviser.PublicId);
        dto.TenantId.ShouldBe(tenant.PublicId);
    }
}
