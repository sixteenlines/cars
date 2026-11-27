using System.Security.Claims;
using System.Text;
using CarTracker.Api.Data;
using CarTracker.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CarTracker.Api.Endpoints;

public record TmmItemDto(string Type, EntryStatus Status, string Title, DateOnly? Date, decimal Cost, bool Duplicate);
public record TmmPreviewDto(
    string FileName, TmmVehicle Vehicle, int? MatchVehicleId, int Mods, int Maintenance, int Expenses, int Planned,
    int FuelUps, int Duplicates, decimal Total, List<string> Warnings, List<TmmItemDto> Items);
public record TmmResultDto(int VehicleId, string VehicleName, int Entries, int FuelUps, int Skipped);

/// <summary>
/// Track My Mods CSV import. Stateless: the preview parses the file and saves nothing; the import gets the same file
/// again plus the target car. Re-importing is safe: entries (date, title, cost) and fill-ups (date, total) that are
/// already on the car are skipped.
/// </summary>
public static class TmmEndpoints
{
    private const long MaxBytes = 2 * 1024 * 1024;

    public static void MapTmmEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/import/tmm").RequireAuthorization();

        g.MapPost("/preview", async ([FromForm] IFormFile file, AppDbContext db, ClaimsPrincipal p) =>
        {
            var (tmm, error) = await Read(file);
            if (error is not null) return error;
            var userId = p.UserId();
            var match = await Match(db, userId, tmm!.Vehicle);
            var existing = match is null ? null : await Existing(db, match.Id);

            var items = tmm.Entries.Select(e => new TmmItemDto(e.Kind.ToString(), e.Status, e.Title, e.Date, e.Cost,
                    existing?.IsDuplicate(e) ?? false))
                .Concat(tmm.FuelUps.Select(f => new TmmItemDto("Fuel", EntryStatus.Done, f.Notes?.Split('\n')[0] ?? "", f.Date, f.Total,
                    existing?.IsDuplicate(f) ?? false)))
                .OrderByDescending(i => i.Date ?? DateOnly.MaxValue).ToList();

            return Results.Ok(new TmmPreviewDto(file.FileName, tmm.Vehicle, match?.Id,
                Mods: tmm.Entries.Count(e => e.Kind == EntryKind.Mod),
                Maintenance: tmm.Entries.Count(e => e.Kind == EntryKind.Maintenance),
                Expenses: tmm.Entries.Count(e => e.Kind == EntryKind.Expense),
                Planned: tmm.Entries.Count(e => e.Status == EntryStatus.Planned),
                FuelUps: tmm.FuelUps.Count,
                Duplicates: items.Count(i => i.Duplicate),
                Total: tmm.Entries.Where(e => e.Status == EntryStatus.Done).Sum(e => e.Cost) + tmm.FuelUps.Sum(f => f.Total),
                tmm.Warnings, items));
        }).DisableAntiforgery();

        // vehicleId empty → create the car from the export's vehicle data.
        g.MapPost("/", async ([FromForm] IFormFile file, [FromForm] int? vehicleId, AppDbContext db, ClaimsPrincipal p) =>
        {
            var (tmm, error) = await Read(file);
            if (error is not null) return error;
            var userId = p.UserId();

            Vehicle vehicle;
            if (vehicleId is { } id)
            {
                var found = await db.Vehicles.FirstOrDefaultAsync(v => v.Id == id && v.OwnerId == userId);
                if (found is null) return Loc.Problem("Vehicle not found.", "Auto nicht gefunden.", 400);
                vehicle = found;
            }
            else
            {
                var s = tmm!.Vehicle;
                vehicle = new Vehicle
                {
                    OwnerId = userId, Name = s.Name, Make = s.Make, Model = s.Model, Year = s.Year, Badge = s.Badge,
                    ColorExterior = s.ColorExterior, ColorInterior = s.ColorInterior, Plate = s.Plate, Vin = s.Vin,
                    PurchasePrice = s.PurchasePrice, PurchaseDate = s.PurchaseDate, PurchaseOdometer = s.PurchaseOdometer,
                    Notes = s.Notes,
                };
                db.Vehicles.Add(vehicle);
            }

            var existing = vehicle.Id > 0 ? await Existing(db, vehicle.Id) : new ExistingData([], []);
            int entries = 0, fuel = 0, skipped = 0;
            // One Tag object per name for the whole file (resolving per entry would create the same new tag twice).
            var names = tmm!.Entries.SelectMany(e => e.Tags).Select(Mapping.NormalizeTag).OfType<string>().Distinct().ToList();
            var tags = await db.Tags.Where(t => t.OwnerId == userId && names.Contains(t.Name)).ToDictionaryAsync(t => t.Name);
            foreach (var n in names.Where(n => !tags.ContainsKey(n))) tags[n] = new Tag { OwnerId = userId, Name = n };
            foreach (var e in tmm.Entries)
            {
                if (existing.IsDuplicate(e)) { skipped++; continue; }
                var entry = new Entry
                {
                    Vehicle = vehicle, Kind = e.Kind, Status = e.Status, Title = e.Title, Date = e.Date, EndDate = e.EndDate,
                    Odometer = e.Odometer, Notes = e.Notes,
                    // With a part number the cost becomes a line item (so it shows and is searchable), else a plain total.
                    ManualTotal = e.PartNumber is null ? e.Cost : null,
                    LineItems = e.PartNumber is null ? [] : [new LineItem { Name = e.Title, PartNumber = e.PartNumber, Quantity = 1, UnitPrice = e.Cost }],
                    Tags = e.Tags.Select(Mapping.NormalizeTag).OfType<string>().Distinct().Take(20).Select(n => tags[n]).ToList(),
                };
                db.Entries.Add(entry);
                entries++;
            }
            foreach (var f in tmm.FuelUps)
            {
                if (existing.IsDuplicate(f)) { skipped++; continue; }
                db.FuelUps.Add(new FuelUp
                {
                    Vehicle = vehicle, Date = f.Date, Odometer = f.Odometer, Total = f.Total, Liters = f.Liters,
                    PricePerLiter = f.Liters is { } l ? Math.Round(f.Total / l, 3) : null, FullTank = true, Notes = f.Notes,
                });
                fuel++;
            }
            await db.SaveChangesAsync();
            return Results.Ok(new TmmResultDto(vehicle.Id, vehicle.Name, entries, fuel, skipped));
        }).DisableAntiforgery();
    }

    private static async Task<(TmmFile?, IResult?)> Read(IFormFile? file)
    {
        if (file is null || file.Length == 0) return (null, Loc.Problem("No file.", "Keine Datei.", 400));
        if (file.Length > MaxBytes) return (null, Loc.Problem("The file is too large for a Track My Mods export.", "Die Datei ist zu groß für einen Track-My-Mods-Export.", 400));
        using var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        try { return (TmmImport.Parse(await reader.ReadToEndAsync()), null); }
        catch (TmmFormatException ex) { return (null, Results.Problem(ex.Message, statusCode: 400)); }
    }

    /// <summary>Same VIN, else same name.</summary>
    private static async Task<Vehicle?> Match(AppDbContext db, string userId, TmmVehicle s)
    {
        var mine = await db.Vehicles.Where(v => v.OwnerId == userId).ToListAsync();
        return (s.Vin is { } vin ? mine.FirstOrDefault(v => string.Equals(v.Vin, vin, StringComparison.OrdinalIgnoreCase)) : null)
            ?? mine.FirstOrDefault(v => string.Equals(v.Name.Trim(), s.Name.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private record ExistingData(HashSet<(DateOnly?, string, decimal)> Entries, HashSet<(DateOnly, decimal)> Fuel)
    {
        public bool IsDuplicate(TmmEntry e) => Entries.Contains((e.Date, e.Title.Trim().ToLowerInvariant(), e.Cost));
        public bool IsDuplicate(TmmFuel f) => Fuel.Contains((f.Date, f.Total));
    }

    private static async Task<ExistingData> Existing(AppDbContext db, int vehicleId)
    {
        var entries = await db.Entries.Where(e => e.VehicleId == vehicleId).Include(e => e.LineItems).ToListAsync();
        var fuel = await db.FuelUps.Where(f => f.VehicleId == vehicleId).Select(f => new { f.Date, f.Total }).ToListAsync();
        return new ExistingData(
            entries.Select(e => (e.Date, e.Title.Trim().ToLowerInvariant(), e.Total)).ToHashSet(),
            fuel.Select(f => (f.Date, f.Total)).ToHashSet());
    }
}
