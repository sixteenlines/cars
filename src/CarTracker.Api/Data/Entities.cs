using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Identity;

namespace CarTracker.Api.Data;

public class AppUser : IdentityUser
{
    public string DisplayName { get; set; } = "";
    public bool IsAdmin { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class Invite
{
    public int Id { get; set; }
    /// <summary>SHA-256 of the invite code; the plain code is only shown once on creation.</summary>
    public string CodeHash { get; set; } = "";
    public string? Note { get; set; }
    public bool GrantsAdmin { get; set; }
    public string? CreatedById { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
    public string? UsedById { get; set; }
}

public class Vehicle
{
    public int Id { get; set; }
    public string OwnerId { get; set; } = "";
    public AppUser? Owner { get; set; }

    public string Name { get; set; } = "";
    public string? Make { get; set; }
    public string? Model { get; set; }
    public int? Year { get; set; }
    public string? Badge { get; set; }
    public string? ColorExterior { get; set; }
    public string? ColorInterior { get; set; }
    public string? Plate { get; set; }
    public string? Vin { get; set; }

    public decimal? PurchasePrice { get; set; }
    public DateOnly? PurchaseDate { get; set; }
    public int? PurchaseOdometer { get; set; }
    public int? CurrentOdometer { get; set; }
    public bool IncludePurchaseInTotals { get; set; }

    public string? Notes { get; set; }
    public int? CoverPhotoId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<Entry> Entries { get; set; } = [];
    public List<Photo> Photos { get; set; } = [];
    public List<FuelUp> FuelUps { get; set; } = [];
}

[JsonConverter(typeof(JsonStringEnumConverter<EntryKind>))]
public enum EntryKind { Mod = 0, Maintenance = 1, Expense = 2 }

[JsonConverter(typeof(JsonStringEnumConverter<EntryStatus>))]
public enum EntryStatus { Planned = 0, Done = 1 }

public class Entry
{
    public int Id { get; set; }
    public int VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }

    public EntryKind Kind { get; set; }
    public EntryStatus Status { get; set; } = EntryStatus.Done;
    public string Title { get; set; } = "";
    public DateOnly? Date { get; set; }
    public DateOnly? EndDate { get; set; }
    public int? Odometer { get; set; }
    public string? Vendor { get; set; }
    public string? Notes { get; set; }
    public string Currency { get; set; } = "EUR";

    /// <summary>Set when the entry has no line items, or to override their sum.</summary>
    public decimal? ManualTotal { get; set; }

    /// <summary>E.g. an installation/labour entry pointing at the mod it belongs to.</summary>
    public int? RelatedEntryId { get; set; }
    public Entry? RelatedEntry { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<LineItem> LineItems { get; set; } = [];
    public List<Tag> Tags { get; set; } = [];
    public List<Photo> Photos { get; set; } = [];
    public List<Document> Documents { get; set; } = [];

    public decimal Total => ManualTotal ?? LineItems.Sum(l => l.Total);
}

public class LineItem
{
    public int Id { get; set; }
    public int EntryId { get; set; }
    public int SortOrder { get; set; }
    public string Name { get; set; } = "";
    public string? Brand { get; set; }
    public string? PartNumber { get; set; }
    public decimal Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public decimal Total => Math.Round(Quantity * UnitPrice, 2);
}

public class Tag
{
    public int Id { get; set; }
    public string OwnerId { get; set; } = "";
    /// <summary>Normalized: lowercase, trimmed, spaces → underscores.</summary>
    public string Name { get; set; } = "";
    public List<Entry> Entries { get; set; } = [];
}

public class Photo
{
    public int Id { get; set; }
    public string OwnerId { get; set; } = "";
    public int VehicleId { get; set; }
    public int? EntryId { get; set; }
    public Entry? Entry { get; set; }

    /// <summary>Relative to the photo storage root, without size suffix.</summary>
    public string StorageKey { get; set; } = "";
    public string? OriginalFileName { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public long SizeBytes { get; set; }
    /// <summary>From EXIF DateTimeOriginal when available (used by the timeline later).</summary>
    public DateTimeOffset? TakenAt { get; set; }
    public string? Caption { get; set; }
    public DateTimeOffset UploadedAt { get; set; } = DateTimeOffset.UtcNow;
}

[JsonConverter(typeof(JsonStringEnumConverter<DocumentStatus>))]
public enum DocumentStatus
{
    /// <summary>Uploaded, waiting for / running AI extraction.</summary>
    Processing = 0,
    /// <summary>Extraction done, waiting for the user to review and import.</summary>
    Ready = 1,
    /// <summary>Extraction failed (or no AI configured) — can still be imported manually.</summary>
    Failed = 2,
    /// <summary>Entries were created from it.</summary>
    Imported = 3,
}

/// <summary>An uploaded bill/invoice (PDF or photo) and what the AI read from it.</summary>
public class Document
{
    public int Id { get; set; }
    public string OwnerId { get; set; } = "";
    /// <summary>Vehicle chosen on upload (or suggested by the AI); final vehicles are on the entries.</summary>
    public int? VehicleId { get; set; }

    public string StorageKey { get; set; } = "";
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = "";

    public DocumentStatus Status { get; set; }
    public string? Error { get; set; }
    /// <summary>Raw JSON as returned by the extractor (see BillExtraction).</summary>
    public string? ExtractionJson { get; set; }

    // Denormalized from the extraction for duplicate detection and lists.
    public string? Vendor { get; set; }
    public string? InvoiceNumber { get; set; }
    public DateOnly? InvoiceDate { get; set; }
    public decimal? Total { get; set; }

    // AI usage accounting.
    public bool AiCalled { get; set; }
    public string? AiModel { get; set; }
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public decimal EstimatedCostUsd { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ImportedAt { get; set; }

    public List<Entry> Entries { get; set; } = [];
}

/// <summary>A fill-up. Kept apart from entries so logging it stays a 10-second job.</summary>
public class FuelUp
{
    public int Id { get; set; }
    public int VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }

    public DateOnly Date { get; set; }
    public int? Odometer { get; set; }
    /// <summary>"95", "98", "100", "102", "diesel" — or null when unknown (e.g. imported).</summary>
    public string? Grade { get; set; }
    public decimal? Liters { get; set; }
    public decimal? PricePerLiter { get; set; }
    public decimal Total { get; set; }
    /// <summary>Only full-to-full fill-ups with odometer readings give a consumption value.</summary>
    public bool FullTank { get; set; } = true;
    public string? Station { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>AI calls that aren't bill documents (fuel scans), for limits and the admin usage view.</summary>
public class AiUsage
{
    public int Id { get; set; }
    public string OwnerId { get; set; } = "";
    public string Purpose { get; set; } = "";
    public string? Model { get; set; }
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public decimal EstimatedCostUsd { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
