using System.Security.Claims;
using CarTracker.Api.Data;
using CarTracker.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace CarTracker.Api.Endpoints;

public record CreateInviteRequest(string? Note, int? ValidDays, bool GrantsAdmin);
public record CreatedInviteResponse(int Id, string Code, DateTimeOffset ExpiresAt);
public record InviteDto(int Id, string? Note, bool GrantsAdmin, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt,
    DateTimeOffset? UsedAt, string? UsedBy);
public record UserDto(string Id, string Email, string DisplayName, bool IsAdmin, DateTimeOffset CreatedAt, int VehicleCount);

public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/admin").RequireAuthorization("Admin");

        g.MapGet("/invites", async (AppDbContext db) =>
        {
            var invites = await db.Invites.OrderByDescending(i => i.CreatedAt).ToListAsync();
            var userNames = await db.Users.ToDictionaryAsync(u => u.Id, u => u.DisplayName);
            return invites.Select(i => new InviteDto(i.Id, i.Note, i.GrantsAdmin, i.CreatedAt, i.ExpiresAt, i.UsedAt,
                i.UsedById is null ? null : userNames.GetValueOrDefault(i.UsedById)));
        });

        g.MapPost("/invites", async (CreateInviteRequest req, AppDbContext db, ClaimsPrincipal p) =>
        {
            var days = Math.Clamp(req.ValidDays ?? 14, 1, 90);
            var code = InviteCodes.Generate();
            var invite = new Invite
            {
                CodeHash = InviteCodes.Hash(code),
                Note = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim(),
                GrantsAdmin = req.GrantsAdmin,
                CreatedById = p.UserId(),
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(days),
            };
            db.Invites.Add(invite);
            await db.SaveChangesAsync();
            return Results.Ok(new CreatedInviteResponse(invite.Id, code, invite.ExpiresAt));
        });

        g.MapDelete("/invites/{id:int}", async (int id, AppDbContext db) =>
        {
            var deleted = await db.Invites.Where(i => i.Id == id && i.UsedAt == null).ExecuteDeleteAsync();
            return deleted == 0 ? Results.NotFound() : Results.NoContent();
        });

        g.MapGet("/users", async (AppDbContext db) =>
        {
            var counts = await db.Vehicles.GroupBy(v => v.OwnerId)
                .Select(x => new { x.Key, Count = x.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count);
            var users = await db.Users.OrderBy(u => u.CreatedAt).ToListAsync();
            return users.Select(u => new UserDto(u.Id, u.Email ?? "", u.DisplayName, u.IsAdmin, u.CreatedAt,
                counts.GetValueOrDefault(u.Id)));
        });
    }
}
