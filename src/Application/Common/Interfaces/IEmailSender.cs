namespace MyWealthV2.Application.Common.Interfaces;

public interface IEmailSender
{
    Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken);
}
