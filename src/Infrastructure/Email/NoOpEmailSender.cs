using MyWealthV2.Application.Common.Interfaces;

namespace MyWealthV2.Infrastructure.Email;

public sealed class NoOpEmailSender : IEmailSender
{
    public Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
