using System.Security.Claims;
using CarTracker.Api.Data;
using CarTracker.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace CarTracker.Api.Endpoints;

public static class TagEndpoints
{
    public static void MapTagEndpoints(this RouteGroupBuilder api)
    {
        // All tags of the current user with usage counts (for suggestions and filter chips).
        api.MapGet("/tags", async (AppDbContext db, ClaimsPrincipal p, int? vehicleId) =>
        {
            var userId = p.UserId();
            var tags = await db.Tags.Where(t => t.OwnerId == userId)
                .Select(t => new TagDto(t.Name,
                    t.Entries.Count(e => vehicleId == null || e.VehicleId == vehicleId)))
                .ToListAsync();
            return tags.Where(t => t.Count > 0 || vehicleId == null)
                .OrderByDescending(t => t.Count).ThenBy(t => t.Name);
        }).RequireAuthorization();
    }
}
