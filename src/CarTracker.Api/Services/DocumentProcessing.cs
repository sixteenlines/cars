using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Channels;
using CarTracker.Api.Data;
using ImageMagick;
using Microsoft.EntityFrameworkCore;

namespace CarTracker.Api.Services;

/// <summary>Stores bill files. PDFs are kept as-is; photos are normalized to JPEG (HEIC works, metadata stripped).</summary>
public class DocumentStorage(StorageOptions storage)
{
    public static readonly string[] AllowedExtensions = [".pdf", ".jpg", ".jpeg", ".png", ".webp", ".heic", ".heif"];

    public record Stored(string StorageKey, string ContentType, long SizeBytes, string Sha256);

    public async Task<Stored> SaveAsync(byte[] bytes, string fileName, string userId, CancellationToken ct)
    {
        var isPdf = bytes.Length > 4 && bytes[0] == '%' && bytes[1] == 'P' && bytes[2] == 'D' && bytes[3] == 'F';
        // Hash the original upload so re-uploading the same file is detected even for photos.
        var sha = Convert.ToHexString(SHA256.HashData(bytes));

        byte[] output;
        string contentType, ext;
        if (isPdf)
        {
            (output, contentType, ext) = (bytes, "application/pdf", ".pdf");
        }
        else
        {
            try
            {
                using var img = new MagickImage(bytes);
                img.AutoOrient();
                img.Strip();
                // Receipts need readable text, not huge files: 2400px on the long side is plenty.
                if (img.Width > 2400 || img.Height > 2400) img.Resize(new MagickGeometry(2400, 2400));
                img.Format = MagickFormat.Jpeg;
                img.Quality = 85;
                output = img.ToByteArray();
            }
            catch (MagickException ex)
            {
                throw new InvalidImageException($"“{fileName}” is neither a PDF nor a readable image.", ex);
            }
            (contentType, ext) = ("image/jpeg", ".jpg");
        }

        var userDir = new string(userId.Where(char.IsLetterOrDigit).ToArray());
        var key = Path.Combine(userDir, DateTime.UtcNow.ToString("yyyy-MM", CultureInfo.InvariantCulture), Guid.NewGuid().ToString("N") + ext);
        var path = FilePath(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, output, ct);
        return new Stored(key, contentType, output.Length, sha);
    }

    public string FilePath(string key) => Path.Combine(storage.DocumentsPath, key);

    public void Delete(string key)
    {
        var path = FilePath(key);
        if (File.Exists(path)) File.Delete(path);
    }
}

/// <summary>In-memory queue of document ids waiting for extraction.</summary>
public class ExtractionQueue
{
    private readonly Channel<int> _channel = Channel.CreateUnbounded<int>();
    public void Enqueue(int documentId) => _channel.Writer.TryWrite(documentId);
    public IAsyncEnumerable<int> ReadAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);
}

/// <summary>
/// Runs extractions in the background so the upload request returns immediately
/// (AI calls can take 10–60 s; Cloudflare cuts proxied requests at 100 s). The UI polls the document.
/// </summary>
public class ExtractionWorker(ExtractionQueue queue, IServiceScopeFactory scopes, ILogger<ExtractionWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Re-queue anything that was interrupted by a restart.
        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            foreach (var id in await db.Documents.Where(d => d.Status == DocumentStatus.Processing).Select(d => d.Id).ToListAsync(stoppingToken))
                queue.Enqueue(id);
        }

        await foreach (var id in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                await ProcessAsync(scope.ServiceProvider, id, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                log.LogError(ex, "Extraction of document {Id} crashed", id);
            }
        }
    }

    public static async Task ProcessAsync(IServiceProvider sp, int id, CancellationToken ct)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var storage = sp.GetRequiredService<DocumentStorage>();
        var extractor = sp.GetRequiredService<IBillExtractor>();

        var doc = await db.Documents.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (doc is null || doc.Status != DocumentStatus.Processing) return;

        ExtractionResult result;
        try
        {
            var bytes = await File.ReadAllBytesAsync(storage.FilePath(doc.StorageKey), ct);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromMinutes(3));
            result = await extractor.ExtractAsync(bytes, doc.ContentType, doc.FileName, timeout.Token);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            result = new(null, "Reading the bill timed out or failed. Try again.", null, 0, 0, 0, false);
        }

        doc.AiCalled |= result.AiCalled;
        doc.AiModel = result.Model ?? doc.AiModel;
        doc.InputTokens += result.InputTokens;
        doc.OutputTokens += result.OutputTokens;
        doc.EstimatedCostUsd += result.EstimatedCostUsd;

        if (result.Data is { } data)
        {
            doc.Status = DocumentStatus.Ready;
            doc.Error = null;
            doc.ExtractionJson = JsonSerializer.Serialize(data);
            doc.Vendor = data.Vendor?.Trim() is { Length: > 0 } v ? v[..Math.Min(v.Length, 200)] : null;
            doc.InvoiceNumber = data.InvoiceNumber?.Trim() is { Length: > 0 } n ? n[..Math.Min(n.Length, 100)] : null;
            doc.InvoiceDate = DateOnly.TryParseExact(data.InvoiceDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
            doc.Total = data.GrandTotal;
            doc.VehicleId ??= await SuggestVehicleAsync(db, doc.OwnerId, data.VehicleHint, ct);
        }
        else
        {
            doc.Status = DocumentStatus.Failed;
            doc.Error = result.Error;
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Matches VIN / plate / name / model mentioned on the bill against the user's cars.</summary>
    public static async Task<int?> SuggestVehicleAsync(AppDbContext db, string ownerId, string? hint, CancellationToken ct)
    {
        var vehicles = await db.Vehicles.Where(v => v.OwnerId == ownerId).ToListAsync(ct);
        if (vehicles.Count == 1) return vehicles[0].Id;
        if (string.IsNullOrWhiteSpace(hint)) return null;

        static string Norm(string? s) => new((s ?? "").Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        var h = Norm(hint);
        if (h.Length == 0) return null;

        return vehicles.FirstOrDefault(v => Norm(v.Vin) is { Length: >= 7 } vin && (h.Contains(vin) || h.Contains(vin[^7..])))?.Id
            ?? vehicles.FirstOrDefault(v => Norm(v.Plate) is { Length: >= 4 } p && h.Contains(p))?.Id
            ?? vehicles.FirstOrDefault(v => Norm(v.Name) is { Length: >= 2 } n && h.Contains(n))?.Id
            ?? vehicles.FirstOrDefault(v => Norm(v.Model) is { Length: >= 2 } m && h.Contains(m))?.Id;
    }
}
