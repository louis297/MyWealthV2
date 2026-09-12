using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Events;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Domain.UnitTests.Entities;

public class TenantTests
{
    [Test]
    public void Disable_RaisesTenantDisabled()
    {
        var tenant = Tenant.Create("Acme", "acme");

        tenant.Disable();

        tenant.IsEnabled.ShouldBeFalse();
        tenant.DomainEvents.OfType<TenantDisabled>().ShouldHaveSingleItem();
    }
}
