using MyWealthV2.Application.Common.Models;

namespace MyWealthV2.Application.Common.Interfaces;

public interface IIdentityService
{
    Task<string?> GetUserNameAsync(string userId);

    Task<bool> IsInRoleAsync(string userId, string role);

    Task<bool> AuthorizeAsync(string userId, string policyName);

    Task<(Result Result, string UserId)> CreateUserAsync(string userName, string password);

    Task<(Result Result, string UserId)> CreateLoginAsync(
        string userName,
        string email,
        string password,
        int? tenantId);

    Task<Result> DeleteUserAsync(string userId);

    Task<Result> ChangePasswordAsync(string identityUserId, string currentPassword, string newPassword);
}
