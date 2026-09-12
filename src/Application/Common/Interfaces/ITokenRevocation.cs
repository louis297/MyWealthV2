namespace MyWealthV2.Application.Common.Interfaces;

public interface ITokenRevocation
{
    Task RevokeSubject(string subject, CancellationToken cancellationToken);
}
