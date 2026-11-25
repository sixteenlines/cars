using System.Security.Claims;
using CarTracker.Api.Data;
using CarTracker.Api.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CarTracker.Api.Endpoints;

public record LoginRequest(string Email, string Password);
public record RegisterRequest(string Code, string DisplayName, string Email, string Password);
public record MeResponse(string Id, string Email, string DisplayName, bool IsAdmin);
public record InviteCheckResponse(bool Valid, string? Note);

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/auth");

        g.MapPost("/login", async (LoginRequest req, SignInManager<AppUser> signIn, UserManager<AppUser> users) =>
        {
            var user = await users.FindByEmailAsync(req.Email.Trim());
            if (user is null) return Results.Problem("Wrong email or password.", statusCode: 401);

            var result = await signIn.PasswordSignInAsync(user, req.Password, isPersistent: true, lockoutOnFailure: true);
            if (result.IsLockedOut) return Results.Problem("Too many failed attempts. Try again in 15 minutes.", statusCode: 423);
            if (!result.Succeeded) return Results.Problem("Wrong email or password.", statusCode: 401);
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

        g.MapGet("/invite/{code}", async (string code, AppDbContext db) =>
        {
            var invite = await FindOpenInvite(db, code);
            return Results.Ok(new InviteCheckResponse(invite is not null, invite?.Note));
        }).RequireRateLimiting("auth");

        g.MapPost("/register", async (RegisterRequest req, AppDbContext db, UserManager<AppUser> users,
            SignInManager<AppUser> signIn) =>
        {
            var invite = await FindOpenInvite(db, req.Code);
            if (invite is null) return Results.Problem("This invite is invalid, used or expired.", statusCode: 400);

            var name = req.DisplayName.Trim();
            if (name.Length is 0 or > 50) return Results.Problem("Please enter a name (max. 50 characters).", statusCode: 400);

            var user = new AppUser
            {
                UserName = req.Email.Trim(),
                Email = req.Email.Trim(),
                DisplayName = name,
                IsAdmin = invite.GrantsAdmin,
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

    private static Task<Invite?> FindOpenInvite(AppDbContext db, string code)
    {
        var hash = InviteCodes.Hash(code);
        var now = DateTimeOffset.UtcNow;
        return db.Invites.FirstOrDefaultAsync(i => i.CodeHash == hash && i.UsedAt == null && i.ExpiresAt > now);
    }

    private static MeResponse ToMe(AppUser u) => new(u.Id, u.Email ?? "", u.DisplayName, u.IsAdmin);
}
