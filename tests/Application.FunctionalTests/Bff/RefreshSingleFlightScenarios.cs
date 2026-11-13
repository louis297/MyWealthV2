using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc.Testing;
using OpenIddict.Abstractions;

namespace MyWealthV2.Application.FunctionalTests.Bff;

/// <summary>
/// Refresh single-flight (bff-adviser-portal amendment 2026-11-13, RF-T1–RF-T7), run against each BFF host.
/// Concurrency is arranged with gates, never with timing.
/// </summary>
public abstract class RefreshSingleFlightScenarios
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private StaleBearerApi _api = null!;
    private RefreshGate _gate = null!;
    private ManualClock _clock = null!;
    private IAsyncDisposable _host = null!;
    private HttpClient _bff = null!;
    private HttpClient _identity = null!;

    protected abstract string ClientId { get; }

    protected abstract string ClientSecret { get; }

    protected abstract string SessionCookie { get; }

    protected static WebApplicationFactoryClientOptions BffClientOptions => new()
    {
        AllowAutoRedirect = false,
        HandleCookies = false
    };

    protected abstract (IAsyncDisposable Host, HttpClient Client) StartBff(
        string authority,
        HttpMessageHandler identity,
        StaleBearerApi api,
        TimeProvider clock,
        string apiBaseAddress);

    protected abstract Task<(string Email, string Password, string TenantCode)> CreatePersonAsync();

    [OneTimeSetUp]
    public async Task Start()
    {
        using var probe = FunctionalTestSetup.Identity.CreateClient();
        _api = new StaleBearerApi();
        _gate = new RefreshGate(FunctionalTestSetup.Identity.Server.CreateHandler());
        _clock = new ManualClock(DateTimeOffset.UtcNow);
        (_host, _bff) = StartBff(
            probe.BaseAddress!.ToString(),
            _gate,
            _api,
            _clock,
            FunctionalTestSetup.WebClient.BaseAddress!.ToString());
        _identity = FunctionalTestSetup.Identity.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
        await RegisterOriginAsync();
    }

    [OneTimeTearDown]
    public async Task Stop()
    {
        _bff.Dispose();
        _identity.Dispose();
        await _host.DisposeAsync();
        _gate.Dispose();
    }

    [SetUp]
    public void Reset()
    {
        _clock.UtcNow = DateTimeOffset.UtcNow;
        _gate.Reset();
        _api.Reset();
    }

    [Test]
    public async Task RF_T1_ConcurrentWebApi401s_ShareOneRefreshGrant()
    {
        var session = await SignInAsync();

        var responses = await ConcurrentRefreshAsync(session);

        responses[0].StatusCode.ShouldBe(HttpStatusCode.OK);
        responses[1].StatusCode.ShouldBe(HttpStatusCode.OK);
        _gate.RefreshGrants.ShouldBe(1);
        var retries = _api.Bearers.Where(bearer => bearer != session.AccessToken).ToList();
        retries.Count.ShouldBe(2);
        retries.Distinct().Count().ShouldBe(1);
        SetsSessionCookie(responses[0]).ShouldBeTrue();
        SetsSessionCookie(responses[1]).ShouldBeTrue();
    }

    [Test]
    public async Task RF_T2_ReplacedRefreshToken_IsAnsweredFromMemory()
    {
        var session = await SignInAsync();
        (await GetMeAsync(session.Cookie)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var refreshed = _gate.LastIssuedAccessToken;

        var again = await GetMeAsync(session.Cookie);

        again.StatusCode.ShouldBe(HttpStatusCode.OK);
        _gate.RefreshGrants.ShouldBe(1);
        _api.Bearers[^1].ShouldBe(refreshed);
        SetsSessionCookie(again).ShouldBeTrue();
    }

    [Test]
    public async Task RF_T3_AfterConcurrentRefresh_TheNewestRefreshTokenStillWorks()
    {
        var session = await SignInAsync();
        await ConcurrentRefreshAsync(session);
        var newest = _gate.LastIssuedRefreshToken;
        newest.ShouldNotBeNullOrWhiteSpace();

        var direct = await RefreshDirectAsync(newest!);

        direct.IsSuccessStatusCode.ShouldBeTrue(await direct.Content.ReadAsStringAsync());
    }

    [Test]
    public async Task RF_T4_ExpiredRememberedAccess_RefreshesTheNewestRefreshToken()
    {
        var session = await SignInAsync();
        (await GetMeAsync(session.Cookie)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var replacement = _gate.LastIssuedRefreshToken;
        _clock.UtcNow += TimeSpan.FromMinutes(16);

        var late = await GetMeAsync(session.Cookie);

        late.StatusCode.ShouldBe(HttpStatusCode.OK);
        _gate.RefreshGrants.ShouldBe(2);
        _gate.PresentedRefreshTokens[^1].ShouldBe(replacement);
    }

    [Test]
    public async Task RF_T5_ReplacedRefreshToken_ThirtyMinutesLater_GoesToTheTokenEndpointAndFails()
    {
        var session = await SignInAsync();
        (await GetMeAsync(session.Cookie)).StatusCode.ShouldBe(HttpStatusCode.OK);
        _clock.UtcNow += TimeSpan.FromMinutes(30);

        var late = await GetMeAsync(session.Cookie);

        late.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        ClearsSessionCookie(late).ShouldBeTrue();
        _gate.RefreshGrants.ShouldBe(2);
        _gate.PresentedRefreshTokens[^1].ShouldBe(session.RefreshToken);
    }

    [Test]
    public async Task RF_T6_FailedRefresh_SignsOutEveryWaiter_AndIsNotRemembered()
    {
        var session = await SignInAsync();
        (await RevokeAsync(session.RefreshToken)).IsSuccessStatusCode.ShouldBeTrue();

        var both = await Task.WhenAll(GetMeAsync(session.Cookie), GetMeAsync(session.Cookie));

        both[0].StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        both[1].StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        ClearsSessionCookie(both[0]).ShouldBeTrue();
        ClearsSessionCookie(both[1]).ShouldBeTrue();
        var grants = _gate.RefreshGrants;

        var third = await GetMeAsync(session.Cookie);

        third.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        _gate.RefreshGrants.ShouldBe(grants + 1);
    }

    [Test]
    public async Task RF_T7_AbortedRequest_DoesNotCancelTheRefreshGrant()
    {
        var session = await SignInAsync();
        _gate.Hold();
        using var abort = new CancellationTokenSource();
        var aborted = GetMeAsync(session.Cookie, abort.Token);
        await _gate.WaitUntilHeldAsync(Patience);
        await abort.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(() => aborted);
        _gate.Release();

        (await _gate.WaitForGrantsAsync(1, Patience)).ShouldBeTrue("the grant must finish after its request is aborted");
        var after = await GetMeAsync(session.Cookie);

        after.StatusCode.ShouldBe(HttpStatusCode.OK);
        _gate.RefreshGrants.ShouldBe(1);
    }

    private async Task<HttpResponseMessage[]> ConcurrentRefreshAsync(Session session)
    {
        _gate.Hold();
        var first = GetMeAsync(session.Cookie);
        var second = GetMeAsync(session.Cookie);
        await _api.WaitForStaleCallsAsync(2, Patience);
        _gate.Release();
        return await Task.WhenAll(first, second);
    }

    private async Task<HttpResponseMessage> GetMeAsync(string cookie, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/users/me");
        request.Headers.TryAddWithoutValidation("Cookie", cookie);
        return await _bff.SendAsync(request, cancellationToken);
    }

    private bool SetsSessionCookie(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values) &&
        values.Any(value =>
            value.StartsWith(SessionCookie + "=", StringComparison.Ordinal) &&
            !value.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase));

    private bool ClearsSessionCookie(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values) &&
        values.Any(value =>
            value.StartsWith(SessionCookie, StringComparison.Ordinal) &&
            value.Contains("expires=", StringComparison.OrdinalIgnoreCase));

    private async Task<Session> SignInAsync()
    {
        var person = await CreatePersonAsync();
        var login = await _bff.GetAsync("/bff/login");
        var callback = await FollowLoginAsync(login.Headers.Location!, person);
        using var callbackRequest = new HttpRequestMessage(HttpMethod.Get, callback.PathAndQuery);
        callbackRequest.Headers.TryAddWithoutValidation("Cookie", CookieHeader(login));
        var signedIn = await _bff.SendAsync(callbackRequest);
        signedIn.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var access = _gate.CodeAccessToken ?? throw new InvalidOperationException("No access token from the code grant.");
        var refresh = _gate.CodeRefreshToken ?? throw new InvalidOperationException("No refresh token from the code grant.");
        _api.MarkStale(access);
        return new Session(CookieHeader(signedIn), access, refresh);
    }

    private async Task<HttpResponseMessage> RefreshDirectAsync(string refreshToken)
    {
        using var body = new FormUrlEncodedContent(new Dictionary<string, string?>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = ClientId,
            ["client_secret"] = ClientSecret
        });
        return await _identity.PostAsync("/connect/token", body);
    }

    private async Task<HttpResponseMessage> RevokeAsync(string refreshToken)
    {
        using var body = new FormUrlEncodedContent(new Dictionary<string, string?>
        {
            ["token"] = refreshToken,
            ["token_type_hint"] = "refresh_token",
            ["client_id"] = ClientId,
            ["client_secret"] = ClientSecret
        });
        return await _identity.PostAsync("/connect/revocation", body);
    }

    private async Task RegisterOriginAsync()
    {
        using var scope = FunctionalTestSetup.Identity.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var application = await manager.FindByClientIdAsync(ClientId)
            ?? throw new InvalidOperationException(ClientId + " is not registered.");
        var descriptor = new OpenIddictApplicationDescriptor();
        await manager.PopulateAsync(descriptor, application);
        descriptor.ClientSecret = ClientSecret;
        var origin = _bff.BaseAddress!.ToString().TrimEnd('/');
        var redirect = new Uri(origin + "/signin-oidc");
        if (!descriptor.RedirectUris.Contains(redirect))
        {
            descriptor.RedirectUris.Add(redirect);
        }

        var postLogout = new Uri(origin + "/");
        if (!descriptor.PostLogoutRedirectUris.Contains(postLogout))
        {
            descriptor.PostLogoutRedirectUris.Add(postLogout);
        }

        await manager.UpdateAsync(application, descriptor);
    }

    private async Task<Uri> FollowLoginAsync(Uri authorize, (string Email, string Password, string TenantCode) person)
    {
        var url = authorize.PathAndQuery;
        var prefix = _bff.BaseAddress!.ToString().TrimEnd('/') + "/signin-oidc";
        for (var i = 0; i < 8; i++)
        {
            var response = await _identity.GetAsync(url);
            if (response.Headers.Location is { } early)
            {
                var earlyTarget = early.IsAbsoluteUri ? early : new Uri(_identity.BaseAddress!, early);
                if (earlyTarget.AbsoluteUri.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return earlyTarget;
                }
            }

            if ((int)response.StatusCode is < 300 or >= 400)
            {
                throw new InvalidOperationException(await response.Content.ReadAsStringAsync());
            }

            var next = response.Headers.Location!;
            url = next.IsAbsoluteUri ? next.PathAndQuery : next.ToString();
            if (url.StartsWith("/login", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }
        }

        var html = await (await _identity.GetAsync(url)).Content.ReadAsStringAsync();
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        if (string.IsNullOrEmpty(token))
        {
            token = Regex.Match(html, "value=\"([^\"]+)\"[^>]*name=\"__RequestVerificationToken\"").Groups[1].Value;
        }

        using var form = new FormUrlEncodedContent(new Dictionary<string, string?>
        {
            ["Email"] = person.Email,
            ["Password"] = person.Password,
            ["TenantCode"] = person.TenantCode,
            ["__RequestVerificationToken"] = token
        });
        var posted = await _identity.PostAsync(url, form);
        for (var i = 0; i < 8; i++)
        {
            var next = posted.Headers.Location ?? throw new InvalidOperationException(await posted.Content.ReadAsStringAsync());
            var target = next.IsAbsoluteUri ? next : new Uri(_identity.BaseAddress!, next);
            if (target.AbsoluteUri.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return target;
            }

            posted.Dispose();
            posted = await _identity.GetAsync(target);
        }

        throw new InvalidOperationException("No callback.");
    }

    private static string CookieHeader(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? string.Join("; ", values.Select(value => value.Split(';', 2)[0]))
            : "";

    private sealed record Session(string Cookie, string AccessToken, string RefreshToken);
}

/// <summary>
/// Fake <c>webapi</c>: 401 for an access token marked stale (or none), 200 for any other Bearer.
/// </summary>
public sealed class StaleBearerApi
{
    private readonly Lock _sync = new();
    private readonly HashSet<string> _stale = new(StringComparer.Ordinal);
    private readonly List<string?> _bearers = [];
    private int _staleCalls;

    public IReadOnlyList<string?> Bearers
    {
        get
        {
            lock (_sync)
            {
                return _bearers.ToList();
            }
        }
    }

    public void Reset()
    {
        lock (_sync)
        {
            _bearers.Clear();
            _staleCalls = 0;
        }
    }

    public void MarkStale(string accessToken)
    {
        lock (_sync)
        {
            _stale.Add(accessToken);
        }
    }

    public async Task WaitForStaleCallsAsync(int count, TimeSpan patience)
    {
        var deadline = DateTime.UtcNow + patience;
        while (Volatile.Read(ref _staleCalls) < count)
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Only {_staleCalls} stale webapi calls arrived; expected {count}.");
            }

            await Task.Delay(10);
        }
    }

    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var bearer = request.Headers.Authorization?.Parameter;
        bool stale;
        lock (_sync)
        {
            _bearers.Add(bearer);
            stale = bearer is null || _stale.Contains(bearer);
            if (stale)
            {
                _staleCalls++;
            }
        }

        return Task.FromResult(stale
            ? Json(HttpStatusCode.Unauthorized, """{"title":"Unauthorized"}""")
            : Json(HttpStatusCode.OK, """{"id":"me"}"""));
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}

/// <summary>
/// Wraps the identity handler. Records code and refresh grants, can hold refresh grants until released,
/// and forwards refresh grants one at a time so a replay is always seen as one.
/// </summary>
public sealed class RefreshGate(HttpMessageHandler inner) : DelegatingHandler(inner)
{
    private readonly Lock _sync = new();
    private readonly SemaphoreSlim _serial = new(1, 1);
    private readonly List<string> _presented = [];
    private TaskCompletionSource _release = Released();
    private TaskCompletionSource _held = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _grants;

    public int RefreshGrants => Volatile.Read(ref _grants);

    public IReadOnlyList<string> PresentedRefreshTokens
    {
        get
        {
            lock (_sync)
            {
                return _presented.ToList();
            }
        }
    }

    public string? CodeAccessToken { get; private set; }

    public string? CodeRefreshToken { get; private set; }

    public string? LastIssuedAccessToken { get; private set; }

    public string? LastIssuedRefreshToken { get; private set; }

    public void Reset()
    {
        lock (_sync)
        {
            _release.TrySetResult();
            _held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _presented.Clear();
            _grants = 0;
            CodeAccessToken = null;
            CodeRefreshToken = null;
            LastIssuedAccessToken = null;
            LastIssuedRefreshToken = null;
        }
    }

    public void Hold()
    {
        lock (_sync)
        {
            _release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    public void Release()
    {
        lock (_sync)
        {
            _release.TrySetResult();
        }
    }

    public Task WaitUntilHeldAsync(TimeSpan patience)
    {
        lock (_sync)
        {
            return _held.Task.WaitAsync(patience);
        }
    }

    public async Task<bool> WaitForGrantsAsync(int count, TimeSpan patience)
    {
        var deadline = DateTime.UtcNow + patience;
        while (RefreshGrants < count)
        {
            if (DateTime.UtcNow > deadline)
            {
                return false;
            }

            await Task.Delay(10);
        }

        return true;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.RequestUri?.AbsolutePath.EndsWith("/connect/token", StringComparison.OrdinalIgnoreCase) != true ||
            request.Content is null)
        {
            return await base.SendAsync(request, cancellationToken);
        }

        var body = await request.Content.ReadAsStringAsync(cancellationToken);
        request.Content = new StringContent(body, Encoding.UTF8, "application/x-www-form-urlencoded");
        var form = HttpUtility.ParseQueryString(body);
        if (form["grant_type"] == "authorization_code")
        {
            var codeResponse = await base.SendAsync(request, cancellationToken);
            var (access, refresh) = await ReadTokensAsync(codeResponse, cancellationToken);
            CodeAccessToken = access;
            CodeRefreshToken = refresh;
            return codeResponse;
        }

        if (form["grant_type"] != "refresh_token")
        {
            return await base.SendAsync(request, cancellationToken);
        }

        Task release;
        lock (_sync)
        {
            release = _release.Task;
            _held.TrySetResult();
        }

        await release.WaitAsync(cancellationToken);
        await _serial.WaitAsync(cancellationToken);
        try
        {
            lock (_sync)
            {
                _presented.Add(form["refresh_token"] ?? "");
            }

            var response = await base.SendAsync(request, cancellationToken);
            var (access, refresh) = await ReadTokensAsync(response, cancellationToken);
            if (access is not null)
            {
                LastIssuedAccessToken = access;
                LastIssuedRefreshToken = refresh;
            }

            Interlocked.Increment(ref _grants);
            return response;
        }
        finally
        {
            _serial.Release();
        }
    }

    private static async Task<(string? Access, string? Refresh)> ReadTokensAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        response.Content = new StringContent(json, Encoding.UTF8, "application/json");
        if (!response.IsSuccessStatusCode)
        {
            return (null, null);
        }

        using var document = JsonDocument.Parse(json);
        var access = document.RootElement.TryGetProperty("access_token", out var a) ? a.GetString() : null;
        var refresh = document.RootElement.TryGetProperty("refresh_token", out var r) ? r.GetString() : null;
        return (access, refresh);
    }

    private static TaskCompletionSource Released()
    {
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        released.TrySetResult();
        return released;
    }
}

public sealed class ManualClock(DateTimeOffset utcNow) : TimeProvider
{
    private long _ticks = utcNow.UtcTicks;

    public DateTimeOffset UtcNow
    {
        get => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);
        set => Interlocked.Exchange(ref _ticks, value.UtcTicks);
    }

    public override DateTimeOffset GetUtcNow() => UtcNow;
}
