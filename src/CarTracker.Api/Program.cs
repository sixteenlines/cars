using System.Threading.RateLimiting;
using CarTracker.Api.Data;
using CarTracker.Api.Endpoints;
using CarTracker.Api.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ---- Storage ---------------------------------------------------------------
var storage = builder.Configuration.GetSection("Storage").Get<StorageOptions>() ?? new StorageOptions();
storage.DataPath = Path.GetFullPath(storage.DataPath);
Directory.CreateDirectory(storage.DataPath);
builder.Services.AddSingleton(storage);

// Persist cookie encryption keys next to the DB, otherwise every container restart logs everybody out.
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(storage.DataPath, "keys")))
    .SetApplicationName("CarTracker");

builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseSqlite($"Data Source={Path.Combine(storage.DataPath, "cartracker.db")}"));

// ---- Identity / auth -------------------------------------------------------
builder.Services
    .AddIdentityCore<AppUser>(o =>
    {
        o.User.RequireUniqueEmail = true;
        o.Password.RequiredLength = 10;
        o.Password.RequireNonAlphanumeric = false;
        o.Password.RequireUppercase = false;
        o.Password.RequireDigit = false;
        o.Lockout.AllowedForNewUsers = true;
        o.Lockout.MaxFailedAccessAttempts = 8;
        o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddErrorDescriber<LocalizedIdentityErrorDescriber>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services
    .AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddCookie(IdentityConstants.ApplicationScheme, o =>
    {
        o.Cookie.Name = "ct_auth";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        o.ExpireTimeSpan = TimeSpan.FromDays(60);
        o.SlidingExpiration = true;
        // API: answer with status codes instead of redirecting to a login page.
        o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = 401; return Task.CompletedTask; };
        o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = 403; return Task.CompletedTask; };
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("Admin", p => p.RequireClaim(AuthClaims.IsAdmin, "true"));
builder.Services.AddScoped<IUserClaimsPrincipalFactory<AppUser>, AppClaimsFactory>();

// ---- Rate limiting (login/register) ----------------------------------------
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.AddPolicy("auth", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ClientIp.Get(ctx),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
});

// ---- Uploads ---------------------------------------------------------------
const long maxUpload = 100L * 1024 * 1024; // matches Cloudflare free plan request limit
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = maxUpload);
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = maxUpload);

builder.Services.AddScoped<PhotoService>();
builder.Services.AddSingleton<DocumentStorage>();

// ---- Bill extraction (AI) ----------------------------------------------------
var ai = builder.Configuration.GetSection("Ai").Get<AiOptions>() ?? new AiOptions();
builder.Services.AddSingleton(ai);
builder.Services.AddSingleton<ExtractionQueue>();
builder.Services.AddHostedService<ExtractionWorker>();
if (ai.Provider.Equals("Fake", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<IBillExtractor, FakeBillExtractor>();
    builder.Services.AddSingleton<IFuelScanner, FakeFuelScanner>();
}
else
{
    builder.Services.AddHttpClient<IBillExtractor, OpenAiBillExtractor>(c => c.Timeout = TimeSpan.FromMinutes(3));
    builder.Services.AddHttpClient<IFuelScanner, OpenAiFuelScanner>(c => c.Timeout = TimeSpan.FromSeconds(90));
}
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

var app = builder.Build();

// ---- Database + first-run bootstrap ----------------------------------------
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
    await Bootstrap.EnsureAdminInviteAsync(db, app.Logger, app.Configuration);
}

// Behind cloudflared the app only sees plain HTTP from the tunnel container; trust its X-Forwarded-* headers
// so the app knows the original request was HTTPS (→ Secure cookies). Only enable when the app port is
// NOT reachable directly from outside.
if (app.Configuration.GetValue<bool>("TrustForwardedHeaders"))
{
    var fwd = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto };
    fwd.KnownIPNetworks.Clear();
    fwd.KnownProxies.Clear();
    app.UseForwardedHeaders(fwd);
}

app.UseExceptionHandler();
// UI language per request (Accept-Language from the SPA). Only the UI culture changes; number/date parsing stays invariant.
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new Microsoft.AspNetCore.Localization.RequestCulture("en"),
    SupportedCultures = [System.Globalization.CultureInfo.GetCultureInfo("en")],
    SupportedUICultures = [System.Globalization.CultureInfo.GetCultureInfo("en"), Loc.German, System.Globalization.CultureInfo.GetCultureInfo("de")],
});
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

var api = app.MapGroup("/api");
api.MapAuthEndpoints();
api.MapAdminEndpoints();
api.MapVehicleEndpoints();
api.MapEntryEndpoints();
api.MapPhotoEndpoints();
api.MapTagEndpoints();
api.MapDocumentEndpoints();
api.MapFuelEndpoints();
api.MapTmmEndpoints();
var appVersion = app.Configuration["AppVersion"] ?? "dev";
api.MapGet("/health", () => Results.Ok(new { status = "ok", version = appVersion }));

// Unknown /api routes are real 404s; everything else is the SPA.
api.MapFallback(() => Results.NotFound());
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program;
