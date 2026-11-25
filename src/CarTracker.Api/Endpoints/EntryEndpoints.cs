using System.Security.Claims;
using CarTracker.Api.Data;
using CarTracker.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace CarTracker.Api.Endpoints;

public static class EntryEndpoints
{
    public static void MapEntryEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/vehicles/{vehicleId:int}/entries", async (int vehicleId, AppDbContext db, ClaimsPrincipal p,
            EntryKind? kind, EntryStatus? status, string? tag, string? q) =>
        {
            if (!await OwnsVehicle(db, vehicleId, p)) return Results.NotFound();

            var query = Full(db).Where(e => e.VehicleId == vehicleId);
            if (kind is not null) query = query.Where(e => e.Kind == kind);
            if (status is not null) query = query.Where(e => e.Status == status);
            if (Mapping.NormalizeTag(tag ?? "") is { } t) query = query.Where(e => e.Tags.Any(x => x.Name == t));

            var entries = await query.ToListAsync();
            if (!string.IsNullOrWhiteSpace(q))
            {
                var needle = q.Trim();
                bool Has(string? s) => s?.Contains(needle, StringComparison.OrdinalIgnoreCase) == true;
                entries = entries.Where(e => Has(e.Title) || Has(e.Vendor) || Has(e.Notes)
                    || e.Tags.Any(x => Has(x.Name))
                    || e.LineItems.Any(l => Has(l.Name) || Has(l.PartNumber) || Has(l.Brand))).ToList();
            }

            // Planned first (no date yet, wishlist), then newest first.
            return Results.Ok(entries
                .OrderBy(e => e.Status == EntryStatus.Planned ? 0 : 1)
                .ThenByDescending(e => e.Date ?? DateOnly.MaxValue)
                .ThenByDescending(e => e.CreatedAt)
                .Select(e => e.ToDto()));
        }).RequireAuthorization();

        var g = api.MapGroup("/entries").RequireAuthorization();

        g.MapGet("/{id:int}", async (int id, AppDbContext db, ClaimsPrincipal p) =>
        {
            var e = await Full(db).FirstOrDefaultAsync(e => e.Id == id && e.Vehicle!.OwnerId == p.UserId());
            if (e is null) return Results.NotFound();
            var linked = await db.Entries.Where(x => x.RelatedEntryId == id).ToListAsync();
            return Results.Ok(e.ToDto(linked));
        });

        api.MapPost("/vehicles/{vehicleId:int}/entries", async (int vehicleId, EntryRequest req, AppDbContext db, ClaimsPrincipal p) =>
        {
            if (!await OwnsVehicle(db, vehicleId, p)) return Results.NotFound();
            if (await Validate(db, req, vehicleId, null) is { } error) return error;

            var e = new Entry { VehicleId = vehicleId };
            await Apply(db, e, req, p.UserId());
            db.Entries.Add(e);
            await db.SaveChangesAsync();
            return Results.Created($"/api/entries/{e.Id}", e.ToDto());
        }).RequireAuthorization();

        g.MapPut("/{id:int}", async (int id, EntryRequest req, AppDbContext db, ClaimsPrincipal p) =>
        {
            var e = await Full(db).FirstOrDefaultAsync(e => e.Id == id && e.Vehicle!.OwnerId == p.UserId());
            if (e is null) return Results.NotFound();
            if (await Validate(db, req, e.VehicleId, id) is { } error) return error;

            db.LineItems.RemoveRange(e.LineItems);
            await Apply(db, e, req, p.UserId());
            e.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(e.ToDto());
        });

        g.MapDelete("/{id:int}", async (int id, AppDbContext db, ClaimsPrincipal p) =>
        {
            // Photos stay in the vehicle gallery (EntryId is set to null).
            var deleted = await db.Entries.Where(e => e.Id == id && e.Vehicle!.OwnerId == p.UserId()).ExecuteDeleteAsync();
            return deleted == 0 ? Results.NotFound() : Results.NoContent();
        });
    }

    private static IQueryable<Entry> Full(AppDbContext db) => db.Entries
        .Include(e => e.LineItems).Include(e => e.Tags).Include(e => e.Photos).Include(e => e.RelatedEntry).Include(e => e.Documents)
        .AsSplitQuery();

    private static Task<bool> OwnsVehicle(AppDbContext db, int vehicleId, ClaimsPrincipal p) =>
        db.Vehicles.AnyAsync(v => v.Id == vehicleId && v.OwnerId == p.UserId());

    internal static async Task<IResult?> Validate(AppDbContext db, EntryRequest r, int vehicleId, int? selfId)
    {
        if (string.IsNullOrWhiteSpace(r.Title) || r.Title.Trim().Length > 200)
            return Results.Problem("Title is required (max. 200 characters).", statusCode: 400);
        if (r.EndDate is not null && r.Date is not null && r.EndDate < r.Date)
            return Results.Problem("End date is before the start date.", statusCode: 400);
        if (r.Odometer is < 0) return Results.Problem("Odometer can't be negative.", statusCode: 400);
        if (r.LineItems?.Any(l => string.IsNullOrWhiteSpace(l.Name)) == true)
            return Results.Problem("Every part needs a name.", statusCode: 400);
        if (r.RelatedEntryId is { } rel && (rel == selfId || !await db.Entries.AnyAsync(e => e.Id == rel && e.VehicleId == vehicleId)))
            return Results.Problem("Linked entry not found on this vehicle.", statusCode: 400);
        return null;
    }

    internal static async Task Apply(AppDbContext db, Entry e, EntryRequest r, string userId)
    {
        e.Kind = r.Kind;
        e.Status = r.Status;
        e.Title = r.Title.Trim();
        e.Date = r.Date;
        e.EndDate = r.EndDate;
        e.Odometer = r.Odometer;
        e.Vendor = r.Vendor.Clean(200);
        e.Notes = r.Notes.Clean(8000);
        e.ManualTotal = r.ManualTotal;
        e.RelatedEntryId = r.RelatedEntryId;

        e.LineItems = (r.LineItems ?? []).Select((l, i) => new LineItem
        {
            SortOrder = i,
            Name = l.Name.Trim(),
            Brand = l.Brand.Clean(100),
            PartNumber = l.PartNumber.Clean(100),
            Quantity = l.Quantity <= 0 ? 1 : l.Quantity,
            UnitPrice = l.UnitPrice,
        }).ToList();

        e.Tags = await ResolveTags(db, userId, r.Tags ?? []);
    }

    public static async Task<List<Tag>> ResolveTags(AppDbContext db, string userId, IEnumerable<string> raw)
    {
        var names = raw.Select(Mapping.NormalizeTag).OfType<string>().Distinct().Take(20).ToList();
        var existing = await db.Tags.Where(t => t.OwnerId == userId && names.Contains(t.Name)).ToListAsync();
        var created = names.Except(existing.Select(t => t.Name)).Select(n => new Tag { OwnerId = userId, Name = n });
        return [.. existing, .. created];
    }
}
