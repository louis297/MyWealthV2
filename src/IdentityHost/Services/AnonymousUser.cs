using MyWealthV2.Application.Common.Interfaces;

namespace MyWealthV2.IdentityHost.Services;

public sealed class AnonymousUser : IUser
{
    public string? Id => null;

    public List<string>? Roles => null;
}
