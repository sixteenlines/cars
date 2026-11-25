using System.Net;
using System.Net.Http.Json;
using CarTracker.Api.Data;
using CarTracker.Api.Endpoints;
using CarTracker.Api.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace CarTracker.Tests;

/// <summary>Boots the real app against a throwaway data directory.</summary>
public class AppFactory : WebApplicationFactory<Program>
{
    public string DataPath { get; } = Path.Combine(Path.GetTempPath(), "cartracker-tests-" + Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder) =>
        builder.UseSetting("Storage:DataPath", DataPath);

    /// <summary>Creates an invite directly in the DB and registers a user with it.</summary>
    public async Task<HttpClient> CreateUserAsync(string email, bool admin = false)
    {
        var code = InviteCodes.Generate();
        using (var scope = Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Invites.Add(new Invite { CodeHash = InviteCodes.Hash(code), GrantsAdmin = admin, ExpiresAt = DateTimeOffset.UtcNow.AddDays(1) });
            await db.SaveChangesAsync();
        }
        var client = CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var res = await client.PostAsJsonAsync("/api/auth/register",
            new RegisterRequest(code, email.Split('@')[0], email, "test-password-123"));
        res.EnsureSuccessStatusCode();
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try { Directory.Delete(DataPath, recursive: true); } catch { /* best effort */ }
    }
}

public class ApiTests(AppFactory factory) : IClassFixture<AppFactory>
{
    private static VehicleRequest Car(string name) =>
        new(name, "BMW", null, null, null, null, null, null, null, null, null, null, null, false, null, null);

    [Fact]
    public async Task Api_requires_login()
    {
        var anon = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/vehicles")).StatusCode);
    }

    [Fact]
    public async Task Invite_can_only_be_used_once()
    {
        var code = InviteCodes.Generate();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Invites.Add(new Invite { CodeHash = InviteCodes.Hash(code), ExpiresAt = DateTimeOffset.UtcNow.AddDays(1) });
            await db.SaveChangesAsync();
        }
        var c = factory.CreateClient();
        var first = await c.PostAsJsonAsync("/api/auth/register", new RegisterRequest(code, "A", "once-a@example.test", "test-password-123"));
        var second = await c.PostAsJsonAsync("/api/auth/register", new RegisterRequest(code, "B", "once-b@example.test", "test-password-123"));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    [Fact]
    public async Task Users_cannot_see_or_edit_each_others_cars()
    {
        var alice = await factory.CreateUserAsync("alice@example.test");
        var bob = await factory.CreateUserAsync("bob@example.test");

        var created = await (await alice.PostAsJsonAsync("/api/vehicles", Car("Alice E46"))).Content.ReadFromJsonAsync<VehicleDto>();
        Assert.NotNull(created);

        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/vehicles/{created.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/vehicles/{created.Id}/entries")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PutAsJsonAsync($"/api/vehicles/{created.Id}", Car("hacked"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.DeleteAsync($"/api/vehicles/{created.Id}")).StatusCode);
        var bobsCars = await bob.GetFromJsonAsync<List<VehicleDto>>("/api/vehicles");
        Assert.Empty(bobsCars!);
    }

    [Fact]
    public async Task Non_admin_cannot_create_invites()
    {
        var user = await factory.CreateUserAsync("plain@example.test");
        var res = await user.PostAsJsonAsync("/api/admin/invites", new CreateInviteRequest(null, null, false));
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Entry_total_and_vehicle_stats_follow_line_items()
    {
        var c = await factory.CreateUserAsync("stats@example.test");
        var car = await (await c.PostAsJsonAsync("/api/vehicles", Car("M2"))).Content.ReadFromJsonAsync<VehicleDto>();

        var mod = new EntryRequest(EntryKind.Mod, EntryStatus.Done, "KW V3", new DateOnly(2025, 12, 29), null, 58750, null, null,
            null, null, ["Suspension", "#coilover "],
            [new LineItemRequest("Fahrwerk", "KW", "35220086", 1, 2240m), new LineItemRequest("Domlager", null, null, 2, 45.5m)]);
        var saved = await (await c.PostAsJsonAsync($"/api/vehicles/{car!.Id}/entries", mod)).Content.ReadFromJsonAsync<EntryDto>();
        Assert.Equal(2331m, saved!.Total);
        Assert.Equal(["coilover", "suspension"], saved.Tags);

        var planned = mod with { Title = "Brakes", Status = EntryStatus.Planned, ManualTotal = 600m, LineItems = [] };
        await c.PostAsJsonAsync($"/api/vehicles/{car.Id}/entries", planned);

        var stats = (await c.GetFromJsonAsync<VehicleDto>($"/api/vehicles/{car.Id}"))!.Stats;
        Assert.Equal(2331m, stats.TotalSpent);   // planned items are not "spent"
        Assert.Equal(600m, stats.PlannedTotal);
        Assert.Equal(58750, stats.LatestOdometer);
    }
}

public class LanguageTests(AppFactory factory) : IClassFixture<AppFactory>
{
    [Fact]
    public async Task Language_setting_is_saved_and_messages_follow_accept_language()
    {
        var c = await factory.CreateUserAsync("lang@example.test");
        var me = await c.GetFromJsonAsync<MeResponse>("/api/auth/me");
        Assert.Null(me!.Language);

        var saved = await (await c.PutAsJsonAsync("/api/auth/settings", new SettingsRequest("de-DE"))).Content.ReadFromJsonAsync<MeResponse>();
        Assert.Equal("de", saved!.Language);

        var bad = new VehicleRequest("", null, null, null, null, null, null, null, null, null, null, null, null, false, null, null);
        c.DefaultRequestHeaders.AcceptLanguage.ParseAdd("de");
        var de = await (await c.PostAsJsonAsync("/api/vehicles", bad)).Content.ReadAsStringAsync();
        Assert.Contains("Ein Name ist nötig", de);

        c.DefaultRequestHeaders.AcceptLanguage.Clear();
        c.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en");
        var en = await (await c.PostAsJsonAsync("/api/vehicles", bad)).Content.ReadAsStringAsync();
        Assert.Contains("Name is required", en);
    }

    [Fact]
    public async Task Registration_errors_are_german_for_german_users()
    {
        var code = InviteCodes.Generate();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Invites.Add(new Invite { CodeHash = InviteCodes.Hash(code), ExpiresAt = DateTimeOffset.UtcNow.AddDays(1) });
            await db.SaveChangesAsync();
        }
        var c = factory.CreateClient();
        c.DefaultRequestHeaders.AcceptLanguage.ParseAdd("de");
        var res = await c.PostAsJsonAsync("/api/auth/register", new RegisterRequest(code, "Kurz", "kurz@example.test", "short", "de"));
        Assert.Contains("mindestens 10 Zeichen", await res.Content.ReadAsStringAsync());
    }
}
