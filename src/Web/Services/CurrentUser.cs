using System.Security.Claims;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MyWealthV2.Web.Services;

public class CurrentUser(IHttpContextAccessor httpContextAccessor, IApplicationDbContext db) : ICurrentUser
{
    private CurrentUserSnapshot? _snapshot;
    private bool _loaded;

    public string? Id => Person?.IdentityUserId;

    public List<string>? Roles => Role is null ? null : [Role.Value.ToString()];

    public Guid? PublicId => Person?.PublicId;

    public UserRole? Role => Person?.Role;

    public int? TenantId => Person?.TenantId;

    public User? Person => Snapshot?.User;

    private CurrentUserSnapshot? Snapshot
    {
        get
        {
            if (_loaded)
            {
                return _snapshot;
            }

            _loaded = true;
            _snapshot = Load();
            return _snapshot;
        }
    }

    private CurrentUserSnapshot? Load()
    {
        var principal = httpContextAccessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var sub = principal.FindFirstValue(AuthClaims.Subject);
        if (!Guid.TryParse(sub, out var publicId))
        {
            return null;
        }

        var user = db.Users.AsNoTracking().SingleOrDefault(person => person.PublicId == publicId);
        Tenant? tenant = null;
        if (user?.TenantId is not null)
        {
            tenant = db.Tenants.AsNoTracking().SingleOrDefault(row => row.Id == user.TenantId);
        }

        return CurrentUserAccess.Resolve(
            user,
            tenant,
            principal.FindFirstValue(AuthClaims.TenantId),
            principal.FindFirstValue(AuthClaims.TenantCode));
    }
}
