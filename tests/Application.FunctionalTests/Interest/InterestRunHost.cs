extern alias WebHost;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyWealthV2.Application.Interest.Accruals;
using MyWealthV2.Application.Interest.Runs;
using MyWealthV2.Domain.Entities;

namespace MyWealthV2.Application.FunctionalTests.Interest;

/// <summary>
/// A second <c>webapi</c> host on the fixture's database with <c>Interest:AllowFutureAsOfDates</c> on (interest-run
/// §9.1: the functional tests that need it turn it on for their host), whose <see cref="IAccountInterest"/> fails for
/// the accounts in <see cref="FailingAccounts"/>.
/// </summary>
internal sealed class InterestRunHost : IDisposable
{
    private readonly WebApplicationFactory<WebHost::Program> _factory;

    public InterestRunHost()
    {
        var identity = FunctionalTestSetup.Identity;
        var authority = identity.ClientOptions.BaseAddress!.ToString().TrimEnd('/');
        _factory = new Infrastructure.WebApiFactory(
                FunctionalTestSetup.ConnectionString,
                authority,
                identity.Server.CreateHandler(),
                settings: new Dictionary<string, string> { [InterestOptions.AllowFutureAsOfDatesKey] = "true" })
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(FailingAccounts);
                services.RemoveAll<IAccountInterest>();
                services.AddScoped<AccountInterest>();
                services.AddScoped<IAccountInterest>(provider => new FailingAccountInterest(
                    provider.GetRequiredService<AccountInterest>(), provider.GetRequiredService<HashSet<int>>()));
            }));
        Client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    /// <summary>Internal Account ids whose accrual throws.</summary>
    public HashSet<int> FailingAccounts { get; } = [];

    public HttpClient Client { get; }

    public void Dispose()
    {
        Client.Dispose();
        _factory.Dispose();
    }

    private sealed class FailingAccountInterest(IAccountInterest inner, HashSet<int> failing) : IAccountInterest
    {
        public Task<AccountInterestResult> AccrueAndPostAsync(
            Account account,
            DateOnly asOfDate,
            DateOnly postingDate,
            Guid runPublicId,
            CancellationToken cancellationToken) =>
            failing.Contains(account.Id)
                ? throw new InvalidOperationException("Test failure for one account.")
                : inner.AccrueAndPostAsync(account, asOfDate, postingDate, runPublicId, cancellationToken);
    }
}
