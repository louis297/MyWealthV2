namespace MyWealthV2.BackOffice;

public interface IAccessTokens
{
    string? AccessToken { get; }

    string? RefreshToken { get; }

    void Store(string accessToken, string refreshToken);
}
