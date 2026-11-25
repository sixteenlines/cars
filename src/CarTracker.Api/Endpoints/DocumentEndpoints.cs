using System.Security.Claims;
using System.Text.Json;
using CarTracker.Api.Data;
using CarTracker.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CarTracker.Api.Endpoints;

public record DocumentDto(
    int Id, int? VehicleId, string FileName, string ContentType, long SizeBytes, DocumentStatus Status, string? Error,
    BillExtraction? Extraction, List<string> Warnings, List<DocumentDuplicate> PossibleDuplicates,
    List<EntryRef> Entries, DateTimeOffset CreatedAt, DateTimeOffset? ImportedAt, bool AlreadyUploaded);

public record DocumentListItem(int Id, int? VehicleId, string FileName, DocumentStatus Status, string? Vendor,
    DateOnly? InvoiceDate, decimal? Total, DateTimeOffset CreatedAt);

public record DocumentDuplicate(int Id, string FileName, DateTimeOffset CreatedAt, DateTimeOffset? ImportedAt);

public record ImportEntry(int VehicleId, EntryRequest Entry);
public record ImportRequest(List<ImportEntry> Entries);

public record UsageDto(string UserId, string DisplayName, int ImportsThisMonth, decimal CostThisMonthUsd, int ImportsTotal, decimal CostTotalUsd,
    int FuelScansThisMonth, int FuelScansTotal);

public static class DocumentEndpoints
{
    private const long MaxFileBytes = 25L * 1024 * 1024;

    public static void MapDocumentEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/documents").RequireAuthorization();

        // Upload a bill → stored + queued for AI extraction. Returns immediately; poll GET /documents/{id}.
        g.MapPost("/", async ([FromForm] IFormFile file, [FromForm] int? vehicleId, AppDbContext db, ClaimsPrincipal p,
            DocumentStorage storage, ExtractionQueue queue, AiOptions ai, CancellationToken ct) =>
        {
            var userId = p.UserId();
            if (file.Length is 0 or > MaxFileBytes) return Loc.Problem("File is empty or larger than 25 MB.", "Die Datei ist leer oder größer als 25 MB.", 400);
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (ext.Length > 0 && !DocumentStorage.AllowedExtensions.Contains(ext))
                return Loc.Problem("Please upload a PDF or a photo (JPG, PNG, HEIC).", "Bitte lade ein PDF oder ein Foto hoch (JPG, PNG, HEIC).", 400);
            if (vehicleId is not null && !await db.Vehicles.AnyAsync(v => v.Id == vehicleId && v.OwnerId == userId, ct))
                return Loc.Problem("Vehicle not found.", "Auto nicht gefunden.", 400);

            byte[] bytes;
            using (var ms = new MemoryStream())
            {
                await file.CopyToAsync(ms, ct);
                bytes = ms.ToArray();
            }

            // Same file uploaded before → reuse it instead of paying for another extraction.
            var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
            var existing = await db.Documents.FirstOrDefaultAsync(d => d.OwnerId == userId && d.Sha256 == sha, ct);
            if (existing is not null)
                return Results.Ok(await ToDto(db, existing, alreadyUploaded: true, ct));

            if (!p.IsAdmin() && ai.MonthlyLimitPerUser > 0)
            {
                var monthStart = new DateTimeOffset(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, TimeSpan.Zero);
                var used = await db.Documents.CountAsync(d => d.OwnerId == userId && d.AiCalled && d.CreatedAt >= monthStart, ct);
                if (used >= ai.MonthlyLimitPerUser)
                    return Loc.Problem($"You've reached this month's limit of {ai.MonthlyLimitPerUser} bill imports. Ask the admin.",
                        $"Du hast das Monatslimit von {ai.MonthlyLimitPerUser} Rechnungsimporten erreicht. Frag den Admin.", 429);
            }

            DocumentStorage.Stored stored;
            try { stored = await storage.SaveAsync(bytes, file.FileName, userId, ct); }
            catch (InvalidImageException ex) { return Results.Problem(ex.Message, statusCode: 400); }

            var doc = new Document
            {
                OwnerId = userId, VehicleId = vehicleId, StorageKey = stored.StorageKey,
                FileName = string.IsNullOrWhiteSpace(file.FileName) ? "bill" : file.FileName.Clean(200)!,
                ContentType = stored.ContentType, SizeBytes = stored.SizeBytes, Sha256 = stored.Sha256,
                Status = DocumentStatus.Processing,
            };
            db.Documents.Add(doc);
            await db.SaveChangesAsync(ct);
            queue.Enqueue(doc.Id);
            return Results.Accepted($"/api/documents/{doc.Id}", await ToDto(db, doc, false, ct));
        }).DisableAntiforgery();

        // Bills that still need a review/import (the "inbox").
        g.MapGet("/", async (AppDbContext db, ClaimsPrincipal p, bool? open) =>
        {
            var userId = p.UserId();
            var q = db.Documents.Where(d => d.OwnerId == userId);
            if (open == true) q = q.Where(d => d.Status != DocumentStatus.Imported);
            var docs = await q.ToListAsync();
            return docs.OrderByDescending(d => d.CreatedAt)
                .Select(d => new DocumentListItem(d.Id, d.VehicleId, d.FileName, d.Status, d.Vendor, d.InvoiceDate, d.Total, d.CreatedAt));
        });

        g.MapGet("/{id:int}", async (int id, AppDbContext db, ClaimsPrincipal p, CancellationToken ct) =>
        {
            var doc = await db.Documents.FirstOrDefaultAsync(d => d.Id == id && d.OwnerId == p.UserId(), ct);
            return doc is null ? Results.NotFound() : Results.Ok(await ToDto(db, doc, false, ct));
        });

        g.MapGet("/{id:int}/file", async (int id, AppDbContext db, ClaimsPrincipal p, DocumentStorage storage, HttpContext http) =>
        {
            var doc = await db.Documents.FirstOrDefaultAsync(d => d.Id == id && d.OwnerId == p.UserId());
            if (doc is null) return Results.NotFound();
            var path = storage.FilePath(doc.StorageKey);
            if (!File.Exists(path)) return Results.NotFound();
            http.Response.Headers.CacheControl = "private, max-age=86400";
            // inline → opens in the browser's PDF viewer instead of downloading.
            http.Response.Headers.ContentDisposition = $"inline; filename=\"{SafeAscii(doc.FileName)}\"";
            return Results.File(path, doc.ContentType);
        });

        g.MapPost("/{id:int}/retry", async (int id, AppDbContext db, ClaimsPrincipal p, ExtractionQueue queue) =>
        {
            var doc = await db.Documents.FirstOrDefaultAsync(d => d.Id == id && d.OwnerId == p.UserId());
            if (doc is null) return Results.NotFound();
            if (doc.Status == DocumentStatus.Imported) return Loc.Problem("Already imported.", "Schon importiert.", 400);
            doc.Status = DocumentStatus.Processing;
            doc.Error = null;
            await db.SaveChangesAsync();
            queue.Enqueue(doc.Id);
            return Results.Accepted($"/api/documents/{doc.Id}");
        });

        // Turn the reviewed bill into one or more entries (possibly on different cars).
        g.MapPost("/{id:int}/import", async (int id, ImportRequest req, AppDbContext db, ClaimsPrincipal p) =>
        {
            var userId = p.UserId();
            var doc = await db.Documents.FirstOrDefaultAsync(d => d.Id == id && d.OwnerId == userId);
            if (doc is null) return Results.NotFound();
            if (doc.Status == DocumentStatus.Processing) return Loc.Problem("The bill is still being read.", "Die Rechnung wird noch gelesen.", 409);
            if (req.Entries is not { Count: > 0 } || req.Entries.Count > 100) return Loc.Problem("Nothing to import.", "Nichts zum Importieren.", 400);

            var vehicleIds = req.Entries.Select(e => e.VehicleId).Distinct().ToList();
            var owned = await db.Vehicles.CountAsync(v => vehicleIds.Contains(v.Id) && v.OwnerId == userId);
            if (owned != vehicleIds.Count) return Loc.Problem("Vehicle not found.", "Auto nicht gefunden.", 400);

            var created = new List<Entry>();
            foreach (var item in req.Entries)
            {
                if (await EntryEndpoints.Validate(db, item.Entry, item.VehicleId, null) is { } error) return error;
                var entry = new Entry { VehicleId = item.VehicleId };
                await EntryEndpoints.Apply(db, entry, item.Entry, userId);
                entry.Documents.Add(doc);
                db.Entries.Add(entry);
                created.Add(entry);
            }
            doc.Status = DocumentStatus.Imported;
            doc.ImportedAt = DateTimeOffset.UtcNow;
            doc.VehicleId = req.Entries[0].VehicleId;
            await db.SaveChangesAsync();
            return Results.Ok(created.Select(e => new EntryRef(e.Id, e.Title)));
        });

        g.MapDelete("/{id:int}", async (int id, AppDbContext db, ClaimsPrincipal p, DocumentStorage storage) =>
        {
            // Entries created from the bill stay; only the attachment goes away.
            var doc = await db.Documents.FirstOrDefaultAsync(d => d.Id == id && d.OwnerId == p.UserId());
            if (doc is null) return Results.NotFound();
            db.Documents.Remove(doc);
            await db.SaveChangesAsync();
            storage.Delete(doc.StorageKey);
            return Results.NoContent();
        });

        api.MapGet("/admin/usage", async (AppDbContext db) =>
        {
            var monthStart = new DateTimeOffset(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, TimeSpan.Zero);
            var docs = await db.Documents.Where(d => d.AiCalled)
                .Select(d => new { d.OwnerId, d.CreatedAt, d.EstimatedCostUsd }).ToListAsync();
            var scans = await db.AiUsages.Select(x => new { x.OwnerId, x.CreatedAt, x.EstimatedCostUsd }).ToListAsync();
            var users = await db.Users.OrderBy(u => u.CreatedAt).ToListAsync();
            return users.Select(u =>
            {
                var mine = docs.Where(d => d.OwnerId == u.Id).ToList();
                var month = mine.Where(d => d.CreatedAt >= monthStart).ToList();
                var myScans = scans.Where(x => x.OwnerId == u.Id).ToList();
                var monthScans = myScans.Where(x => x.CreatedAt >= monthStart).ToList();
                return new UsageDto(u.Id, u.DisplayName,
                    month.Count, month.Sum(d => d.EstimatedCostUsd) + monthScans.Sum(x => x.EstimatedCostUsd),
                    mine.Count, mine.Sum(d => d.EstimatedCostUsd) + myScans.Sum(x => x.EstimatedCostUsd),
                    monthScans.Count, myScans.Count);
            });
        }).RequireAuthorization("Admin");
    }

    private static async Task<DocumentDto> ToDto(AppDbContext db, Document d, bool alreadyUploaded, CancellationToken ct)
    {
        var extraction = d.ExtractionJson is null ? null : JsonSerializer.Deserialize<BillExtraction>(d.ExtractionJson, BillExtraction.Json);

        var duplicates = new List<DocumentDuplicate>();
        if (d.InvoiceNumber is not null)
        {
            var others = await db.Documents
                .Where(o => o.OwnerId == d.OwnerId && o.Id != d.Id && o.InvoiceNumber == d.InvoiceNumber)
                .ToListAsync(ct);
            duplicates = others
                .Where(o => d.Vendor is null || o.Vendor is null || string.Equals(o.Vendor, d.Vendor, StringComparison.OrdinalIgnoreCase))
                .Select(o => new DocumentDuplicate(o.Id, o.FileName, o.CreatedAt, o.ImportedAt)).ToList();
        }

        var entries = await db.Entries.Where(e => e.Documents.Any(x => x.Id == d.Id))
            .Select(e => new EntryRef(e.Id, e.Title)).ToListAsync(ct);

        return new DocumentDto(d.Id, d.VehicleId, d.FileName, d.ContentType, d.SizeBytes, d.Status, d.Error, extraction,
            extraction is null ? [] : BillChecks.Warnings(extraction), duplicates, entries, d.CreatedAt, d.ImportedAt, alreadyUploaded);
    }

    private static string SafeAscii(string s) => new(s.Select(c => c is >= ' ' and < (char)127 and not '"' ? c : '_').ToArray());
}
