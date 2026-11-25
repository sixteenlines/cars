using System.Globalization;
using ImageMagick;

namespace CarTracker.Api.Services;

public record ProcessedPhoto(string StorageKey, int Width, int Height, long SizeBytes, DateTimeOffset? TakenAt);

/// <summary>
/// Normalizes any uploaded image (JPEG, PNG, WebP, iPhone HEIC, …) into a JPEG + thumbnail.
/// Rotation from EXIF is applied, then all metadata (incl. GPS) is stripped.
/// </summary>
public class PhotoService(StorageOptions storage)
{
    public const int FullMax = 2560;
    public const int ThumbMax = 480;

    public async Task<ProcessedPhoto> SaveAsync(Stream input, string userId, CancellationToken ct)
    {
        using var image = new MagickImage();
        try
        {
            await image.ReadAsync(input, ct);
        }
        catch (MagickException ex)
        {
            throw new InvalidImageException(Loc.T("Unsupported or corrupt image.", "Nicht unterstütztes oder beschädigtes Bild."), ex);
        }

        var takenAt = ReadTakenAt(image);
        image.AutoOrient();
        image.Strip();
        image.ColorSpace = ColorSpace.sRGB;

        if (image.Width > FullMax || image.Height > FullMax)
            image.Resize(new MagickGeometry(FullMax, FullMax));

        var key = Path.Combine(Sanitize(userId), DateTime.UtcNow.ToString("yyyy-MM"), Guid.NewGuid().ToString("N"));
        var fullPath = FilePath(key, "full");
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        image.Format = MagickFormat.Jpeg;
        image.Quality = 85;
        await image.WriteAsync(fullPath, ct);

        using (var thumb = image.Clone())
        {
            thumb.Resize(new MagickGeometry(ThumbMax, ThumbMax));
            thumb.Quality = 78;
            await thumb.WriteAsync(FilePath(key, "thumb"), ct);
        }

        return new ProcessedPhoto(key, (int)image.Width, (int)image.Height, new FileInfo(fullPath).Length, takenAt);
    }

    public string FilePath(string storageKey, string size) =>
        Path.Combine(storage.PhotosPath, $"{storageKey}.{(size == "thumb" ? "thumb" : "full")}.jpg");

    public void DeleteFiles(string storageKey)
    {
        foreach (var size in new[] { "full", "thumb" })
        {
            var path = FilePath(storageKey, size);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static DateTimeOffset? ReadTakenAt(MagickImage image)
    {
        var exif = image.GetExifProfile();
        var raw = exif?.GetValue(ExifTag.DateTimeOriginal)?.Value ?? exif?.GetValue(ExifTag.DateTime)?.Value;
        if (raw is null) return null;
        // EXIF format: "2026:03:13 14:22:05" (local time of the camera, no zone).
        return DateTime.TryParseExact(raw.Trim(), "yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var dt)
            ? new DateTimeOffset(dt, TimeSpan.Zero)
            : null;
    }

    private static string Sanitize(string s) => new(s.Where(char.IsLetterOrDigit).ToArray());
}

public class InvalidImageException(string message, Exception inner) : Exception(message, inner);
