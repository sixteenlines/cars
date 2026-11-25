using System.Security.Claims;
using CarTracker.Api.Data;
using CarTracker.Api.Services;
using ImageMagick;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CarTracker.Api.Endpoints;

public record FuelUpRequest(DateOnly? Date, int? Odometer, string? Grade, decimal? Liters, decimal? PricePerLiter, decimal? Total,
    bool FullTank, string? Station, string? Notes);

public record FuelUpDto(int Id, int VehicleId, DateOnly Date, int? Odometer, string? Grade, decimal? Liters, decimal? PricePerLiter,
    decimal Total, bool FullTank, string? Station, string? Notes,
    /// <summary>Set on full fill-ups that close a full-to-full stretch.</summary>
    decimal? Consumption, int? DistanceKm, bool ConsumptionPlausible);

public record FuelLogDto(List<FuelUpDto> FuelUps, FuelMath.Stats Stats);

public record FuelScanDto(string Source, decimal? Total, decimal? Liters, decimal? PricePerLiter, string? Grade,
    string? ProductName, string? Station, DateOnly? Date, List<string> Warnings);

public static class FuelEndpoints
{
    public const string ScanPurpose = "fuel";

    public static void MapFuelEndpoints(this RouteGroupBuilder api)
    {
        var vg = api.MapGroup("/vehicles/{vehicleId:int}/fuel").RequireAuthorization();
        var g = api.MapGroup("/fuel").RequireAuthorization();

        vg.MapGet("/", async (int vehicleId, AppDbContext db, ClaimsPrincipal p) =>
        {
            if (!await OwnsVehicle(db, p, vehicleId)) return Results.NotFound();
            var fills = await db.FuelUps.Where(f => f.VehicleId == vehicleId).ToListAsync();
            return Results.Ok(Log(fills));
        });

        vg.MapPost("/", async (int vehicleId, FuelUpRequest req, AppDbContext db, ClaimsPrincipal p) =>
        {
            if (!await OwnsVehicle(db, p, vehicleId)) return Results.NotFound();
            var f = new FuelUp { VehicleId = vehicleId };
            if (Apply(f, req) is { } error) return error;
            db.FuelUps.Add(f);
            await db.SaveChangesAsync();
            return Results.Created($"/api/fuel/{f.Id}", await DtoWithContext(db, f));
        });

        g.MapGet("/{id:int}", async (int id, AppDbContext db, ClaimsPrincipal p) =>
            await Find(db, p, id) is { } f ? Results.Ok(await DtoWithContext(db, f)) : Results.NotFound());

        g.MapPut("/{id:int}", async (int id, FuelUpRequest req, AppDbContext db, ClaimsPrincipal p) =>
        {
            if (await Find(db, p, id) is not { } f) return Results.NotFound();
            if (Apply(f, req) is { } error) return error;
            await db.SaveChangesAsync();
            return Results.Ok(await DtoWithContext(db, f));
        });

        g.MapDelete("/{id:int}", async (int id, AppDbContext db, ClaimsPrincipal p) =>
        {
            if (await Find(db, p, id) is not { } f) return Results.NotFound();
            db.FuelUps.Remove(f);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // Photo of the pump display or receipt → values for the form. Synchronous (one small image, a few seconds);
        // the photo is not stored.
        g.MapPost("/scan", async ([FromForm] IFormFile file, AppDbContext db, ClaimsPrincipal p, IFuelScanner scanner,
            AiOptions ai, CancellationToken ct) =>
        {
            var userId = p.UserId();
            if (file.Length is 0 or > 25L * 1024 * 1024) return Loc.Problem("Photo is empty or larger than 25 MB.", "Das Foto ist leer oder größer als 25 MB.", 400);

            if (!p.IsAdmin() && ai.MonthlyFuelScansPerUser > 0)
            {
                var monthStart = new DateTimeOffset(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, TimeSpan.Zero);
                var used = await db.AiUsages.CountAsync(u => u.OwnerId == userId && u.Purpose == ScanPurpose && u.CreatedAt >= monthStart, ct);
                if (used >= ai.MonthlyFuelScansPerUser)
                    return Loc.Problem($"You've reached this month's limit of {ai.MonthlyFuelScansPerUser} fuel scans. Type the numbers instead.",
                        $"Du hast das Monatslimit von {ai.MonthlyFuelScansPerUser} Tank-Scans erreicht. Tipp die Werte stattdessen ein.", 429);
            }

            byte[] jpeg;
            try
            {
                using var ms = new MemoryStream();
                await file.CopyToAsync(ms, ct);
                jpeg = FuelImage.Prepare(ms.ToArray());
            }
            catch (MagickException) { return Loc.Problem("That file isn't a readable photo.", "Die Datei ist kein lesbares Foto.", 400); }

            // Stay well below Cloudflare's 100 s proxy timeout.
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(80));
            var r = await scanner.ScanAsync(jpeg, cts.Token);

            if (r.AiCalled)
            {
                db.AiUsages.Add(new AiUsage
                {
                    OwnerId = userId, Purpose = ScanPurpose, Model = r.Model,
                    InputTokens = r.InputTokens, OutputTokens = r.OutputTokens, EstimatedCostUsd = r.EstimatedCostUsd,
                });
                await db.SaveChangesAsync(CancellationToken.None);
            }

            if (r.Data is null) return Results.Problem(r.Error ?? Loc.T("Couldn't read the photo.", "Das Foto konnte nicht gelesen werden."), statusCode: 502);
            if (!r.Data.IsFuel || r.Data is { Total: null, Liters: null, PricePerLiter: null })
                return Loc.Problem("Couldn't find fuel numbers on this photo. Try again closer to the display, or type them.", "Auf dem Foto waren keine Tankwerte zu finden. Versuch es näher an der Anzeige oder tipp sie ein.", 422);
            return Results.Ok(ToScanDto(r.Data));
        }).DisableAntiforgery();
    }

    /// <summary>Normalizes what the AI read: completes the missing value and flags numbers that don't add up.</summary>
    public static FuelScanDto ToScanDto(FuelScan s)
    {
        var warnings = new List<string>();
        var (total, liters, ppl) = FuelMath.Complete(s.Total, s.Liters, s.PricePerLiter);
        if (total is > 0 && liters is > 0 && ppl is > 0 && Math.Abs(liters.Value * ppl.Value - total.Value) > Math.Max(0.05m, total.Value * 0.01m))
            warnings.Add(Loc.T("Litres × price per litre doesn't match the total. Check the numbers.", "Liter × Literpreis passt nicht zum Betrag. Prüf die Werte."));
        if (ppl is < 0.8m or > 3.5m) warnings.Add(Loc.T("Price per litre looks unusual.", "Der Literpreis sieht ungewöhnlich aus."));
        if (liters is > 150) warnings.Add(Loc.T("That's a lot of litres. Check the number.", "Das sind sehr viele Liter. Prüf den Wert."));
        var grade = s.Grade is not null && FuelMath.Grades.Contains(s.Grade) ? s.Grade : null;
        DateOnly? date = DateOnly.TryParse(s.Date, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : null;
        return new FuelScanDto(s.Source, total, liters, ppl, grade, s.ProductName, s.Station?.Trim(), date, warnings);
    }

    public static FuelLogDto Log(List<FuelUp> fills)
    {
        var segs = FuelMath.Segments(fills).ToDictionary(s => s.FuelUpId);
        var list = FuelMath.Chronological(fills).AsEnumerable().Reverse()
            .Select(f => ToDto(f, segs.GetValueOrDefault(f.Id))).ToList();
        return new FuelLogDto(list, FuelMath.Compute(fills, DateOnly.FromDateTime(DateTime.Now)));
    }

    private static FuelUpDto ToDto(FuelUp f, FuelMath.Segment? s) => new(
        f.Id, f.VehicleId, f.Date, f.Odometer, f.Grade, f.Liters, f.PricePerLiter, f.Total, f.FullTank, f.Station, f.Notes,
        s?.Consumption, s?.DistanceKm, s?.Plausible ?? false);

    private static async Task<FuelUpDto> DtoWithContext(AppDbContext db, FuelUp f)
    {
        var fills = await db.FuelUps.Where(x => x.VehicleId == f.VehicleId).ToListAsync();
        var seg = FuelMath.Segments(fills).FirstOrDefault(s => s.FuelUpId == f.Id);
        return ToDto(f, seg);
    }

    private static IResult? Apply(FuelUp f, FuelUpRequest r)
    {
        var grade = string.IsNullOrWhiteSpace(r.Grade) ? null : r.Grade.Trim().ToLowerInvariant();
        if (grade is not null && !FuelMath.Grades.Contains(grade))
            return Loc.Problem("Unknown fuel grade.", "Unbekannte Kraftstoffsorte.", 400);
        if (r.Total is <= 0 || r.Liters is <= 0 || r.PricePerLiter is <= 0)
            return Loc.Problem("Amounts must be greater than zero.", "Beträge müssen größer als null sein.", 400);

        var (total, liters, ppl) = FuelMath.Complete(r.Total, r.Liters, r.PricePerLiter);
        if (total is null) return Loc.Problem("Enter the total, or litres and price per litre.", "Gib den Betrag ein oder Liter und Literpreis.", 400);
        if (r.Odometer is < 0 or > 5_000_000) return Loc.Problem("Odometer looks wrong.", "Der Kilometerstand sieht falsch aus.", 400);

        f.Date = r.Date ?? DateOnly.FromDateTime(DateTime.Now);
        f.Odometer = r.Odometer;
        f.Grade = grade;
        f.Total = Math.Round(total.Value, 2);
        f.Liters = liters is null ? null : Math.Round(liters.Value, 2);
        f.PricePerLiter = ppl is null ? null : Math.Round(ppl.Value, 3);
        f.FullTank = r.FullTank;
        f.Station = r.Station.Clean(100);
        f.Notes = r.Notes.Clean(1000);
        return null;
    }

    private static Task<bool> OwnsVehicle(AppDbContext db, ClaimsPrincipal p, int vehicleId) =>
        db.Vehicles.AnyAsync(v => v.Id == vehicleId && v.OwnerId == p.UserId());

    private static Task<FuelUp?> Find(AppDbContext db, ClaimsPrincipal p, int id) =>
        db.FuelUps.FirstOrDefaultAsync(f => f.Id == id && f.Vehicle!.OwnerId == p.UserId());
}
