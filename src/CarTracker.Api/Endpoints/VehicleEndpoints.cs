using System.Security.Claims;
using CarTracker.Api.Data;
using CarTracker.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace CarTracker.Api.Endpoints;

public static class VehicleEndpoints
{
    public static void MapVehicleEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/vehicles").RequireAuthorization();

        g.MapGet("/", async (AppDbContext db, ClaimsPrincipal p) =>
        {
            var vehicles = await WithStatsData(db).Where(v => v.OwnerId == p.UserId())
                .OrderBy(v => v.CreatedAt).ToListAsync();
            return vehicles.Select(v => v.ToDto());
        });

        g.MapGet("/{id:int}", async (int id, AppDbContext db, ClaimsPrincipal p) =>
        {
            var v = await WithStatsData(db).FirstOrDefaultAsync(v => v.Id == id && v.OwnerId == p.UserId());
            return v is null ? Results.NotFound() : Results.Ok(v.ToDto());
        });

        g.MapPost("/", async (VehicleRequest req, AppDbContext db, ClaimsPrincipal p) =>
        {
            if (Validate(req) is { } error) return error;
            var v = new Vehicle { OwnerId = p.UserId() };
            Apply(v, req);
            v.CoverPhotoId = null; // no photos yet
            db.Vehicles.Add(v);
            await db.SaveChangesAsync();
            return Results.Created($"/api/vehicles/{v.Id}", v.ToDto());
        });

        g.MapPut("/{id:int}", async (int id, VehicleRequest req, AppDbContext db, ClaimsPrincipal p) =>
        {
            if (Validate(req) is { } error) return error;
            var v = await WithStatsData(db).FirstOrDefaultAsync(v => v.Id == id && v.OwnerId == p.UserId());
            if (v is null) return Results.NotFound();
            Apply(v, req);
            if (v.CoverPhotoId is { } cover && v.Photos.All(ph => ph.Id != cover)) v.CoverPhotoId = null;
            await db.SaveChangesAsync();
            return Results.Ok(v.ToDto());
        });

        g.MapDelete("/{id:int}", async (int id, AppDbContext db, ClaimsPrincipal p, PhotoService photos) =>
        {
            var v = await db.Vehicles.Include(v => v.Photos).FirstOrDefaultAsync(v => v.Id == id && v.OwnerId == p.UserId());
            if (v is null) return Results.NotFound();
            var keys = v.Photos.Select(ph => ph.StorageKey).ToList();
            db.Vehicles.Remove(v);
            await db.SaveChangesAsync();
            foreach (var key in keys) photos.DeleteFiles(key);
            return Results.NoContent();
        });
    }

    private static IQueryable<Vehicle> WithStatsData(AppDbContext db) => db.Vehicles
        .Include(v => v.Entries).ThenInclude(e => e.LineItems)
        .Include(v => v.Photos)
        .Include(v => v.FuelUps)
        .AsSplitQuery();

    private static IResult? Validate(VehicleRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Name) || r.Name.Trim().Length > 100)
            return Loc.Problem("Name is required (max. 100 characters).", "Ein Name ist nötig (max. 100 Zeichen).", 400);
        if (r.Year is < 1886 or > 2100) return Loc.Problem("Year looks wrong.", "Das Baujahr sieht falsch aus.", 400);
        return null;
    }

    private static void Apply(Vehicle v, VehicleRequest r)
    {
        v.Name = r.Name.Trim();
        v.Make = r.Make.Clean(100);
        v.Model = r.Model.Clean(100);
        v.Year = r.Year;
        v.Badge = r.Badge.Clean(100);
        v.ColorExterior = r.ColorExterior.Clean(100);
        v.ColorInterior = r.ColorInterior.Clean(100);
        v.Plate = r.Plate.Clean(20);
        v.Vin = r.Vin.Clean(17)?.ToUpperInvariant();
        v.PurchasePrice = r.PurchasePrice;
        v.PurchaseDate = r.PurchaseDate;
        v.PurchaseOdometer = r.PurchaseOdometer;
        v.CurrentOdometer = r.CurrentOdometer;
        v.IncludePurchaseInTotals = r.IncludePurchaseInTotals;
        v.Notes = r.Notes.Clean(4000);
        v.CoverPhotoId = r.CoverPhotoId;
    }
}
