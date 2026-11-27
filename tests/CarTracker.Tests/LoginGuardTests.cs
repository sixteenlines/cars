using System.Net;
using System.Net.Http.Json;
using CarTracker.Api.Endpoints;
using CarTracker.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace CarTracker.Tests;

/// <summary>Same app, but an IP is blocked after 3 failures (the per-minute rate limit allows 10).</summary>
public class LoginGuardAppFactory : AppFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Security:MaxFailures", "3");
    }
}

public class LoginGuardApiTests(LoginGuardAppFactory factory) : IClassFixture<LoginGuardAppFactory>
{
    private HttpClient From(string ip)
    {
        var c = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        c.DefaultRequestHeaders.Add("CF-Connecting-IP", ip);
        return c;
    }

    private static Task<HttpResponseMessage> Login(HttpClient c, string email, string password) =>
        c.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));

    [Fact]
    public async Task Ip_with_too_many_failed_logins_is_blocked_even_with_the_right_password()
    {
        await factory.CreateUserAsync("guard@example.test");
        var attacker = From("203.0.113.7");

        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(attacker, "guard@example.test", "wrong-password-1")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(attacker, "nobody@example.test", "wrong-password-2")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(attacker, "guard@example.test", "wrong-password-3")).StatusCode);

        Assert.Equal(HttpStatusCode.TooManyRequests, (await Login(attacker, "guard@example.test", "test-password-123")).StatusCode);
        // The account itself isn't locked: the owner on another connection still gets in.
        Assert.Equal(HttpStatusCode.OK, (await Login(From("198.51.100.20"), "guard@example.test", "test-password-123")).StatusCode);
    }

    [Fact]
    public async Task Guessing_invite_codes_gets_an_ip_blocked_from_registering()
    {
        var c = From("203.0.113.8");
        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.OK, (await c.GetAsync($"/api/auth/invite/guess-{i}")).StatusCode);

        var res = await c.PostAsJsonAsync("/api/auth/register", new RegisterRequest("guess-4", "X", "x@example.test", "test-password-123"));
        Assert.Equal(HttpStatusCode.TooManyRequests, res.StatusCode);
    }

    [Fact]
    public async Task Successful_login_clears_earlier_mistakes()
    {
        await factory.CreateUserAsync("typo@example.test");
        var c = From("203.0.113.9");
        for (var round = 0; round < 3; round++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await Login(c, "typo@example.test", "oops-wrong-pw")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await Login(c, "typo@example.test", "oops-wrong-pw")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await Login(c, "typo@example.test", "test-password-123")).StatusCode);
        }
    }
}

public class LoginGuardTests
{
    private sealed class ManualTime : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static (LoginGuard, ManualTime) Guard()
    {
        var time = new ManualTime();
        return (new LoginGuard(new LoginGuardOptions { MaxFailures = 3, WindowMinutes = 60, BanHours = 24 }, time, NullLogger<LoginGuard>.Instance), time);
    }

    [Fact]
    public void Failures_older_than_the_window_dont_count()
    {
        var (g, time) = Guard();
        g.Failure("ip", "x");
        g.Failure("ip", "x");
        time.Now = time.Now.AddMinutes(61);
        g.Failure("ip", "x");
        Assert.False(g.IsBlocked("ip"));
        g.Failure("ip", "x");
        g.Failure("ip", "x");
        Assert.True(g.IsBlocked("ip"));
    }

    [Fact]
    public void Ban_ends_after_the_ban_time_and_a_success_during_it_doesnt_lift_it()
    {
        var (g, time) = Guard();
        for (var i = 0; i < 3; i++) g.Failure("ip", "x");
        g.Success("ip");
        time.Now = time.Now.AddHours(23);
        Assert.True(g.IsBlocked("ip"));
        Assert.False(g.IsBlocked("other-ip"));
        time.Now = time.Now.AddHours(2);
        Assert.False(g.IsBlocked("ip"));
    }
}
