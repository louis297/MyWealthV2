using System.Text.Json;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Enums;
using ForbiddenAccessException = MyWealthV2.Application.Common.Exceptions.ForbiddenAccessException;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.Reports;

public static class AdviserReportScope
{
    public static async Task EnsureAsync(
        IApplicationDbContext db,
        int tenantId,
        Guid adviserId,
        UserRole role,
        Guid? callerPublicId,
        CancellationToken cancellationToken)
    {
        var adviser = await db.Users.AsNoTracking()
                          .SingleOrDefaultAsync(
                              row => row.PublicId == adviserId
                                     && row.TenantId == tenantId
                                     && row.Role == UserRole.Adviser,
                              cancellationToken)
                      ?? throw new NotFoundException("Adviser", adviserId);

        if (role == UserRole.Adviser && callerPublicId != adviser.PublicId)
        {
            throw new NotFoundException("Adviser", adviser.PublicId);
        }
    }

    public static async Task EnsureSnapshotAsync(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        string parametersJson,
        string resourceName,
        CancellationToken cancellationToken)
    {
        var role = currentUser.Role ?? throw new UnauthorizedAccessException();
        var tenantId = currentUser.TenantId ?? throw new ForbiddenAccessException();
        AdviserBalancesBinding binding;
        try
        {
            using var parameters = JsonDocument.Parse(parametersJson);
            binding = AdviserBalancesBinder.Bind(parameters.RootElement, UserRole.TenantAdmin, callerPublicId: null);
        }
        catch (JsonException)
        {
            throw new NotFoundException(resourceName, "parameters");
        }
        catch (ValidationException)
        {
            throw new NotFoundException(resourceName, "parameters");
        }

        await EnsureAsync(db, tenantId, binding.AdviserId, role, currentUser.PublicId, cancellationToken);
    }
}
