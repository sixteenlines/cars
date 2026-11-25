using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using CarTracker.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CarTracker.Api.Services;

public class StorageOptions
{
    /// <summary>Everything persistent lives here: SQLite DB + uploaded files. One dir to back up.</summary>
    public string DataPath { get; set; } = "./_data";
    public string PhotosPath => Path.Combine(DataPath, "files", "photos");
    public string DocumentsPath => Path.Combine(DataPath, "files", "documents");
}

public static class AuthClaims
{
    public const string IsAdmin = "ct:admin";
    public const string DisplayName = "ct:name";
}

public class AppClaimsFactory(UserManager<AppUser> users, IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<AppUser>(users, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(AppUser user)
    {
        var id = await base.GenerateClaimsAsync(user);
        id.AddClaim(new Claim(AuthClaims.IsAdmin, user.IsAdmin ? "true" : "false"));
        id.AddClaim(new Claim(AuthClaims.DisplayName, user.DisplayName));
        return id;
    }
}

public static class UserExtensions
{
    public static string UserId(this ClaimsPrincipal p) =>
        p.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new InvalidOperationException("Not authenticated");

    public static bool IsAdmin(this ClaimsPrincipal p) => p.FindFirstValue(AuthClaims.IsAdmin) == "true";
}

public static class ClientIp
{
    /// <summary>Behind Cloudflare Tunnel the real client IP is in CF-Connecting-IP.</summary>
    public static string Get(HttpContext ctx) =>
        ctx.Request.Headers["CF-Connecting-IP"].FirstOrDefault()
        ?? ctx.Connection.RemoteIpAddress?.ToString()
        ?? "unknown";
}

public static class InviteCodes
{
    public static string Generate() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(18)).Replace('+', '-').Replace('/', '_');

    public static string Hash(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code.Trim())));
}

public static class Bootstrap
{
    /// <summary>
    /// On a fresh install (no users) print a one-time admin invite to the log,
    /// so nobody can register as admin just by being first to reach the site.
    /// </summary>
    public static async Task EnsureAdminInviteAsync(AppDbContext db, ILogger log, IConfiguration config)
    {
        if (await db.Users.AnyAsync()) return;

        // Previous bootstrap invites are replaced on every start until the admin exists.
        var openAdminInvites = await db.Invites.Where(i => i.GrantsAdmin && i.UsedAt == null).ToListAsync();
        db.Invites.RemoveRange(openAdminInvites);

        var code = InviteCodes.Generate();
        db.Invites.Add(new Invite
        {
            CodeHash = InviteCodes.Hash(code),
            GrantsAdmin = true,
            Note = "Bootstrap admin invite",
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
        });
        await db.SaveChangesAsync();

        var baseUrl = config["PublicUrl"]?.TrimEnd('/') ?? "";
        log.LogWarning("No users yet. Create the admin account here (valid 24h, new one on each restart): {Url}",
            $"{baseUrl}/register?code={code}");
    }
}
