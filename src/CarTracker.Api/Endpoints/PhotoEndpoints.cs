using System.Security.Claims;
using CarTracker.Api.Data;
using CarTracker.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CarTracker.Api.Endpoints;

public record PhotoUpdateRequest(string? Caption, int? EntryId);
public record PhotoUploadResult(List<PhotoDto> Photos, List<string> Failed);

public static class PhotoEndpoints
{
    private const long MaxFileBytes = 40L * 1024 * 1024;

    public static void MapPhotoEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/vehicles/{vehicleId:int}/photos", async (int vehicleId, AppDbContext db, ClaimsPrincipal p) =>
        {
            var userId = p.UserId();
            var photos = await db.Photos.Where(ph => ph.VehicleId == vehicleId && ph.OwnerId == userId).ToListAsync();
            return photos.OrderByDescending(ph => ph.TakenAt ?? ph.UploadedAt).Select(ph => ph.ToDto());
        }).RequireAuthorization();

        // Multipart: one or more "files", optional "entryId".
        api.MapPost("/vehicles/{vehicleId:int}/photos", async (int vehicleId, [FromForm] IFormFileCollection files,
            [FromForm] int? entryId, AppDbContext db, ClaimsPrincipal p, PhotoService svc, ILogger<PhotoService> log,
            CancellationToken ct) =>
        {
            var userId = p.UserId();
            var vehicle = await db.Vehicles.FirstOrDefaultAsync(v => v.Id == vehicleId && v.OwnerId == userId, ct);
            if (vehicle is null) return Results.NotFound();
            if (entryId is not null && !await db.Entries.AnyAsync(e => e.Id == entryId && e.VehicleId == vehicleId, ct))
                return Results.Problem("Entry not found on this vehicle.", statusCode: 400);
            if (files.Count == 0) return Results.Problem("No files uploaded.", statusCode: 400);

            var saved = new List<Photo>();
            var failed = new List<string>();
            foreach (var file in files)
            {
                if (file.Length is 0 or > MaxFileBytes) { failed.Add(file.FileName); continue; }
                try
                {
                    await using var stream = file.OpenReadStream();
                    var result = await svc.SaveAsync(stream, userId, ct);
                    var photo = new Photo
                    {
                        OwnerId = userId, VehicleId = vehicleId, EntryId = entryId,
                        StorageKey = result.StorageKey, OriginalFileName = file.FileName.Clean(200),
                        Width = result.Width, Height = result.Height, SizeBytes = result.SizeBytes, TakenAt = result.TakenAt,
                    };
                    db.Photos.Add(photo);
                    saved.Add(photo);
                }
                catch (InvalidImageException ex)
                {
                    log.LogInformation(ex, "Rejected upload {File}", file.FileName);
                    failed.Add(file.FileName);
                }
            }
            await db.SaveChangesAsync(ct);

            // First photo of a vehicle becomes its cover automatically.
            if (vehicle.CoverPhotoId is null && saved.Count > 0)
            {
                vehicle.CoverPhotoId = saved[0].Id;
                await db.SaveChangesAsync(ct);
            }

            return Results.Ok(new PhotoUploadResult(saved.Select(ph => ph.ToDto()).ToList(), failed));
        }).RequireAuthorization().DisableAntiforgery();

        api.MapGet("/photos/{id:int}/{size}", async (int id, string size, AppDbContext db, ClaimsPrincipal p,
            PhotoService svc) =>
        {
            var photo = await db.Photos.FirstOrDefaultAsync(ph => ph.Id == id && ph.OwnerId == p.UserId());
            if (photo is null) return Results.NotFound();
            var path = svc.FilePath(photo.StorageKey, size);
            if (!File.Exists(path)) return Results.NotFound();
            // Files never change for a given id → cache aggressively, but only in the user's browser.
            return Results.File(path, "image/jpeg", enableRangeProcessing: false,
                lastModified: photo.UploadedAt);
        }).RequireAuthorization().AddEndpointFilter(async (ctx, next) =>
        {
            var result = await next(ctx);
            ctx.HttpContext.Response.Headers.CacheControl = "private, max-age=31536000, immutable";
            return result;
        });

        api.MapPatch("/photos/{id:int}", async (int id, PhotoUpdateRequest req, AppDbContext db, ClaimsPrincipal p) =>
        {
            var photo = await db.Photos.FirstOrDefaultAsync(ph => ph.Id == id && ph.OwnerId == p.UserId());
            if (photo is null) return Results.NotFound();
            if (req.EntryId is not null && !await db.Entries.AnyAsync(e => e.Id == req.EntryId && e.VehicleId == photo.VehicleId))
                return Results.Problem("Entry not found on this vehicle.", statusCode: 400);
            photo.Caption = req.Caption.Clean(300);
            photo.EntryId = req.EntryId;
            await db.SaveChangesAsync();
            return Results.Ok(photo.ToDto());
        }).RequireAuthorization();

        api.MapDelete("/photos/{id:int}", async (int id, AppDbContext db, ClaimsPrincipal p, PhotoService svc) =>
        {
            var photo = await db.Photos.FirstOrDefaultAsync(ph => ph.Id == id && ph.OwnerId == p.UserId());
            if (photo is null) return Results.NotFound();
            await db.Vehicles.Where(v => v.CoverPhotoId == id)
                .ExecuteUpdateAsync(s => s.SetProperty(v => v.CoverPhotoId, (int?)null));
            db.Photos.Remove(photo);
            await db.SaveChangesAsync();
            svc.DeleteFiles(photo.StorageKey);
            return Results.NoContent();
        }).RequireAuthorization();
    }
}
