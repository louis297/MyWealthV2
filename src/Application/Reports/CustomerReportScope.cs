using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Application.Reports;

public static class CustomerReportScope
{
    public static void Ensure(User customer, UserRole role, ICurrentUser currentUser)
    {
        if (role == UserRole.Customer && currentUser.PublicId != customer.PublicId)
        {
            throw new NotFoundException("Customer", customer.PublicId);
        }

        if (role == UserRole.Adviser && customer.AdviserId != currentUser.Person?.Id)
        {
            throw new NotFoundException("Customer", customer.PublicId);
        }
    }
}
