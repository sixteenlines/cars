using System.Security.Claims;
using CarTracker.Api.Data;
using CarTracker.Api.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CarTracker.Api.Endpoints;

public record LoginRequest(string Email, string Password);
public record RegisterRequest(string Code, string DisplayName, string Email, string Password, string? Language = null);
public record SettingsRequest(string? Language);
public record MeResponse(string Id, string Email, string DisplayName, bool IsAdmin, string? Language);
public record InviteCheckResponse(bool Valid, string? Note);

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/auth");

        g.MapPost("/login", async (LoginRequest req, SignInManager<AppUser> signIn, UserManager<AppUser> users,
            LoginGuard guard, HttpContext ctx) =>
        {
            var ip = ClientIp.Get(ctx);
            if (guard.IsBlocked(ip)) return Blocked();
            var email = req.Email.Trim();
            var what = $"login for {(email.Length > 100 ? email[..100] : email)}";

            var user = await users.FindByEmailAsync(email);
            if (user is null)
            {
                guard.Failure(ip, what + " (unknown email)");
                return Loc.Problem("Wrong email or password.", "E-Mail oder Passwort ist falsch.", 401);
            }

            var result = await signIn.PasswordSignInAsync(user, req.Password, isPersistent: true, lockoutOnFailure: true);
            if (!result.Succeeded) guard.Failure(ip, what + (result.IsLockedOut ? " (account locked)" : ""));
            if (result.IsLockedOut) return Loc.Problem("Too many failed attempts. Try again in 15 minutes.", "Zu viele Fehlversuche. Versuch es in 15 Minuten nochmal.", 423);
            if (!result.Succeeded) return Loc.Problem("Wrong email or password.", "E-Mail oder Passwort ist falsch.", 401);
            guard.Success(ip);
            return Results.Ok(ToMe(user));
        }).RequireRateLimiting("auth");

        g.MapPost("/logout", async (SignInManager<AppUser> signIn) =>
        {
            await signIn.SignOutAsync();
            return Results.NoContent();
        });

        g.MapGet("/me", async (ClaimsPrincipal p, UserManager<AppUser> users) =>
        {
            if (p.Identity?.IsAuthenticated != true) return Results.Unauthorized();
            var user = await users.GetUserAsync(p);
            return user is null ? Results.Unauthorized() : Results.Ok(ToMe(user));
        });

        g.MapPut("/settings", async (SettingsRequest req, ClaimsPrincipal p, UserManager<AppUser> users) =>
        {
            var user = await users.GetUserAsync(p);
            if (user is null) return Results.Unauthorized();
            user.Language = Loc.Normalize(req.Language);
            await users.UpdateAsync(user);
            return Results.Ok(ToMe(user));
        }).RequireAuthorization();

        g.MapGet("/invite/{code}", async (string code, AppDbContext db, LoginGuard guard, HttpContext ctx) =>
        {
            var ip = ClientIp.Get(ctx);
            if (guard.IsBlocked(ip)) return Blocked();
            var invite = await FindOpenInvite(db, code);
            if (invite is null) guard.Failure(ip, "invite code check");
            return Results.Ok(new InviteCheckResponse(invite is not null, invite?.Note));
        }).RequireRateLimiting("auth");

        g.MapPost("/register", async (RegisterRequest req, AppDbContext db, UserManager<AppUser> users,
            SignInManager<AppUser> signIn, LoginGuard guard, HttpContext ctx) =>
        {
            var ip = ClientIp.Get(ctx);
            if (guard.IsBlocked(ip)) return Blocked();
            var invite = await FindOpenInvite(db, req.Code);
            if (invite is null) guard.Failure(ip, "registration (invalid invite code)");
            if (invite is null) return Loc.Problem("This invite is invalid, used or expired.", "Diese Einladung ist ungültig, schon benutzt oder abgelaufen.", 400);

            var name = req.DisplayName.Trim();
            if (name.Length is 0 or > 50) return Loc.Problem("Please enter a name (max. 50 characters).", "Bitte gib einen Namen ein (max. 50 Zeichen).", 400);

            var user = new AppUser
            {
                UserName = req.Email.Trim(),
                Email = req.Email.Trim(),
                DisplayName = name,
                IsAdmin = invite.GrantsAdmin,
                Language = Loc.Normalize(req.Language),
            };
            var created = await users.CreateAsync(user, req.Password);
            if (!created.Succeeded)
                return Results.Problem(string.Join(" ", created.Errors.Select(e => e.Description)), statusCode: 400);

            invite.UsedAt = DateTimeOffset.UtcNow;
            invite.UsedById = user.Id;
            await db.SaveChangesAsync();

            await signIn.SignInAsync(user, isPersistent: true);
            return Results.Ok(ToMe(user));
        }).RequireRateLimiting("auth");
    }

    private static IResult Blocked() => Loc.Problem(
        "Too many failed attempts from your connection. Try again later.",
        "Zu viele Fehlversuche von deiner Verbindung. Versuch es später nochmal.", 429);

    private static Task<Invite?> FindOpenInvite(AppDbContext db, string code)
    {
        var hash = InviteCodes.Hash(code);
        var now = DateTimeOffset.UtcNow;
        return db.Invites.FirstOrDefaultAsync(i => i.CodeHash == hash && i.UsedAt == null && i.ExpiresAt > now);
    }

    private static MeResponse ToMe(AppUser u) => new(u.Id, u.Email ?? "", u.DisplayName, u.IsAdmin, u.Language);
}
