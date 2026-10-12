using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Infrastructure.Identity;
using OpenIddict.Abstractions;

namespace MyWealthV2.Application.FunctionalTests.Bff;

public class BffRequestBodyTests
{
    private const string RequestHeader = "X-MyWealth-Request";

    private BodyListener _downstream = null!;
    private BffFactory _factory = null!;
    private HttpClient _bff = null!;
    private HttpClient _identity = null!;
    private string _sessionCookie = "";

    [OneTimeSetUp]
    public async Task Start()
    {
        _downstream = BodyListener.Start();
        using var probe = FunctionalTestSetup.Identity.CreateClient();
        _factory = new BffFactory(
            probe.BaseAddress!.ToString(),
            FunctionalTestSetup.Identity.Server.CreateHandler(),
            apiBaseAddress: _downstream.BaseAddress);
        _bff = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false
        });
        _identity = FunctionalTestSetup.Identity.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
        await RegisterBffOriginAsync();
        _sessionCookie = await SignInAsync();
    }

    [OneTimeTearDown]
    public async Task Stop()
    {
        _bff.Dispose();
        _identity.Dispose();
        await _factory.DisposeAsync();
        await _downstream.DisposeAsync();
    }

    [Test]
    public async Task CreateAccount_ForwardsTheRequestBody()
    {
        var customerId = Guid.NewGuid();
        var json = $$"""{"customerId":"{{customerId}}","name":"Cash","type":"Cash","currency":"AUD"}""";
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/accounts");
        request.Headers.TryAddWithoutValidation("Cookie", _sessionCookie);
        request.Headers.TryAddWithoutValidation(RequestHeader, "1");
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _bff.SendAsync(request);

        var responseText = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, responseText);
        responseText.ShouldBe("""{"id":"account"}""");
        var forwarded = await _downstream.Received.WaitAsync(TimeSpan.FromSeconds(5));
        forwarded.Path.ShouldBe("/accounts");
        forwarded.Body.ShouldBe(json);
        forwarded.ContentLength.ShouldBe(Encoding.UTF8.GetByteCount(json));
        forwarded.ContentType.ShouldNotBeNull();
        forwarded.ContentType.ShouldContain("application/json");
    }

    private async Task RegisterBffOriginAsync()
    {
        using var scope = FunctionalTestSetup.Identity.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var application = await manager.FindByClientIdAsync(Services.AdviserPortal)
            ?? throw new InvalidOperationException("adviser-portal is not registered.");
        var descriptor = new OpenIddictApplicationDescriptor();
        await manager.PopulateAsync(descriptor, application);
        descriptor.ClientSecret = AdviserPortalTestSecret.Value;
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

    private async Task<string> SignInAsync()
    {
        var login = await _bff.GetAsync("/bff/login?returnUrl=/customers");
        login.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var cookie = CookieHeader(login);
        var callback = await SignInUntilCallbackAsync(login.Headers.Location!);
        using var callbackRequest = new HttpRequestMessage(HttpMethod.Get, callback.PathAndQuery);
        callbackRequest.Headers.TryAddWithoutValidation("Cookie", cookie);
        var signedIn = await _bff.SendAsync(callbackRequest);
        signedIn.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        return CookieHeader(signedIn);
    }

    private async Task<Uri> SignInUntilCallbackAsync(Uri authorize)
    {
        var url = authorize.IsAbsoluteUri ? authorize.PathAndQuery : authorize.ToString();
        for (var i = 0; i < 8; i++)
        {
            var response = await _identity.GetAsync(url);
            if ((int)response.StatusCode is < 300 or >= 400)
            {
                var body = await response.Content.ReadAsStringAsync();
                throw new InvalidOperationException($"Authorize did not reach hosted login. {(int)response.StatusCode} {body}");
            }

            var next = response.Headers.Location ?? throw new InvalidOperationException("Authorize redirect had no Location.");
            url = next.IsAbsoluteUri ? next.PathAndQuery : next.ToString();
            if (url.StartsWith("/login", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }
        }

        var loginHtml = await (await _identity.GetAsync(url)).Content.ReadAsStringAsync();
        var token = Regex.Match(loginHtml, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        if (string.IsNullOrEmpty(token))
        {
            token = Regex.Match(loginHtml, "value=\"([^\"]+)\"[^>]*name=\"__RequestVerificationToken\"").Groups[1].Value;
        }

        using var form = new FormUrlEncodedContent(new Dictionary<string, string?>
        {
            ["Email"] = DevelopmentIdentitySeeder.SystemAdminEmail,
            ["Password"] = DevelopmentIdentitySeeder.SystemAdminPassword,
            ["TenantCode"] = "",
            ["__RequestVerificationToken"] = token
        });
        var posted = await _identity.PostAsync(url, form);
        var callbackPrefix = _bff.BaseAddress!.ToString().TrimEnd('/') + "/signin-oidc";
        for (var i = 0; i < 8; i++)
        {
            if (posted.Headers.Location is not { } next)
            {
                var body = await posted.Content.ReadAsStringAsync();
                throw new InvalidOperationException($"Login did not return a code. {(int)posted.StatusCode} {body}");
            }

            var target = next.IsAbsoluteUri ? next : new Uri(_identity.BaseAddress!, next);
            if (target.AbsoluteUri.StartsWith(callbackPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return target;
            }

            posted.Dispose();
            posted = await _identity.GetAsync(target);
        }

        throw new InvalidOperationException("Authorize did not return a code.");
    }

    private static string CookieHeader(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values))
        {
            return "";
        }

        return string.Join("; ", values.Select(value => value.Split(';', 2)[0]));
    }

    private sealed class BodyListener : IAsyncDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly TaskCompletionSource<Forwarded> _received = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Task _accept;

        private BodyListener(int port)
        {
            BaseAddress = $"http://127.0.0.1:{port}";
            _listener.Prefixes.Add(BaseAddress + "/");
            _listener.IgnoreWriteExceptions = true;
            _listener.Start();
            _accept = Task.Run(AcceptAsync);
        }

        public string BaseAddress { get; }

        public Task<Forwarded> Received => _received.Task;

        public static BodyListener Start()
        {
            var tcp = new TcpListener(IPAddress.Loopback, 0);
            tcp.Start();
            var port = ((IPEndPoint)tcp.LocalEndpoint).Port;
            tcp.Stop();
            return new BodyListener(port);
        }

        public async ValueTask DisposeAsync()
        {
            _listener.Stop();
            _listener.Close();
            try
            {
                await _accept;
            }
            catch (ObjectDisposedException)
            {
            }
            catch (HttpListenerException)
            {
            }
        }

        private async Task AcceptAsync()
        {
            try
            {
                var context = await _listener.GetContextAsync();
                var length = context.Request.ContentLength64;
                var buffer = length > 0 ? new byte[length] : [];
                var read = 0;
                while (read < buffer.Length)
                {
                    var count = await context.Request.InputStream.ReadAsync(buffer.AsMemory(read));
                    if (count == 0)
                    {
                        break;
                    }

                    read += count;
                }

                var payload = """{"id":"account"}""";
                var responseBytes = Encoding.UTF8.GetBytes(payload);
                context.Response.StatusCode = (int)HttpStatusCode.Created;
                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = responseBytes.Length;
                await context.Response.OutputStream.WriteAsync(responseBytes);
                context.Response.Close();
                _received.TrySetResult(new Forwarded(
                    context.Request.Url?.AbsolutePath ?? "",
                    Encoding.UTF8.GetString(buffer.AsSpan(0, read)),
                    length,
                    context.Request.ContentType));
            }
            catch (Exception exception) when (exception is ObjectDisposedException or HttpListenerException)
            {
                _received.TrySetException(exception);
            }
        }
    }

    private sealed record Forwarded(string Path, string Body, long ContentLength, string? ContentType);
}
