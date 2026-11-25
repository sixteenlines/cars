using CarTracker.Api.Data;
using CarTracker.Api.Services;

namespace CarTracker.Api.Endpoints;

public record VehicleStats(
    decimal TotalSpent, decimal ModsTotal, decimal MaintenanceTotal, decimal ExpensesTotal, decimal PlannedTotal,
    int ModCount, int EntryCount, int PlannedCount, DateOnly? LastMaintenanceDate, int? LatestOdometer, int PhotoCount,
    decimal FuelTotal, int FuelCount, DateOnly? LastFuelDate, decimal? AvgConsumption);

public record VehicleDto(
    int Id, string Name, string? Make, string? Model, int? Year, string? Badge,
    string? ColorExterior, string? ColorInterior, string? Plate, string? Vin,
    decimal? PurchasePrice, DateOnly? PurchaseDate, int? PurchaseOdometer, int? CurrentOdometer,
    bool IncludePurchaseInTotals, string? Notes, int? CoverPhotoId, VehicleStats Stats);

public record VehicleRequest(
    string Name, string? Make, string? Model, int? Year, string? Badge,
    string? ColorExterior, string? ColorInterior, string? Plate, string? Vin,
    decimal? PurchasePrice, DateOnly? PurchaseDate, int? PurchaseOdometer, int? CurrentOdometer,
    bool IncludePurchaseInTotals, string? Notes, int? CoverPhotoId);

public record LineItemDto(int? Id, string Name, string? Brand, string? PartNumber, decimal Quantity, decimal UnitPrice, decimal Total);
public record LineItemRequest(string Name, string? Brand, string? PartNumber, decimal Quantity, decimal UnitPrice);

public record PhotoDto(int Id, int VehicleId, int? EntryId, int Width, int Height, DateTimeOffset? TakenAt,
    DateTimeOffset UploadedAt, string? Caption);

public record EntryRef(int Id, string Title);
public record DocumentRef(int Id, string FileName, string ContentType, string? Vendor, DateOnly? InvoiceDate);

public record EntryDto(
    int Id, int VehicleId, EntryKind Kind, EntryStatus Status, string Title, DateOnly? Date, DateOnly? EndDate,
    int? Odometer, string? Vendor, string? Notes, string Currency, decimal? ManualTotal, decimal Total,
    EntryRef? RelatedEntry, List<EntryRef> LinkedEntries, List<string> Tags, List<LineItemDto> LineItems,
    List<PhotoDto> Photos, List<DocumentRef> Documents, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public record EntryRequest(
    EntryKind Kind, EntryStatus Status, string Title, DateOnly? Date, DateOnly? EndDate, int? Odometer,
    string? Vendor, string? Notes, decimal? ManualTotal, int? RelatedEntryId,
    List<string>? Tags, List<LineItemRequest>? LineItems);

public record TagDto(string Name, int Count);

public static class Mapping
{
    public static PhotoDto ToDto(this Photo p) =>
        new(p.Id, p.VehicleId, p.EntryId, p.Width, p.Height, p.TakenAt, p.UploadedAt, p.Caption);

    public static EntryDto ToDto(this Entry e, IEnumerable<Entry>? linked = null) => new(
        e.Id, e.VehicleId, e.Kind, e.Status, e.Title, e.Date, e.EndDate, e.Odometer, e.Vendor, e.Notes, e.Currency,
        e.ManualTotal, e.Total,
        e.RelatedEntry is null ? null : new EntryRef(e.RelatedEntry.Id, e.RelatedEntry.Title),
        linked?.Select(l => new EntryRef(l.Id, l.Title)).ToList() ?? [],
        e.Tags.Select(t => t.Name).Order().ToList(),
        e.LineItems.OrderBy(l => l.SortOrder)
            .Select(l => new LineItemDto(l.Id, l.Name, l.Brand, l.PartNumber, l.Quantity, l.UnitPrice, l.Total)).ToList(),
        e.Photos.OrderBy(p => p.TakenAt ?? p.UploadedAt).Select(p => p.ToDto()).ToList(),
        e.Documents.Select(d => new DocumentRef(d.Id, d.FileName, d.ContentType, d.Vendor, d.InvoiceDate)).ToList(),
        e.CreatedAt, e.UpdatedAt);

    public static VehicleStats Stats(Vehicle v)
    {
        var done = v.Entries.Where(e => e.Status == EntryStatus.Done).ToList();
        var mods = done.Where(e => e.Kind == EntryKind.Mod).Sum(e => e.Total);
        var maint = done.Where(e => e.Kind == EntryKind.Maintenance).Sum(e => e.Total);
        var exp = done.Where(e => e.Kind == EntryKind.Expense).Sum(e => e.Total);
        var purchase = v.IncludePurchaseInTotals ? v.PurchasePrice ?? 0 : 0;
        var planned = v.Entries.Where(e => e.Status == EntryStatus.Planned).ToList();
        var fuel = FuelMath.Compute(v.FuelUps, DateOnly.FromDateTime(DateTime.Now));

        int?[] odos = [v.CurrentOdometer, v.PurchaseOdometer, .. v.Entries.Select(e => e.Odometer), fuel.LastOdometer];
        var latestOdo = odos.Where(o => o.HasValue).Select(o => o!.Value).DefaultIfEmpty().Max();

        return new VehicleStats(
            TotalSpent: mods + maint + exp + purchase + fuel.TotalCost,
            ModsTotal: mods, MaintenanceTotal: maint, ExpensesTotal: exp,
            PlannedTotal: planned.Sum(e => e.Total),
            ModCount: done.Count(e => e.Kind == EntryKind.Mod),
            EntryCount: v.Entries.Count,
            PlannedCount: planned.Count,
            LastMaintenanceDate: done.Where(e => e.Kind == EntryKind.Maintenance).Max(e => e.Date),
            LatestOdometer: latestOdo == 0 ? null : latestOdo,
            PhotoCount: v.Photos.Count,
            FuelTotal: fuel.TotalCost, FuelCount: fuel.Count, LastFuelDate: fuel.LastDate, AvgConsumption: fuel.AvgConsumption);
    }

    public static VehicleDto ToDto(this Vehicle v) => new(
        v.Id, v.Name, v.Make, v.Model, v.Year, v.Badge, v.ColorExterior, v.ColorInterior, v.Plate, v.Vin,
        v.PurchasePrice, v.PurchaseDate, v.PurchaseOdometer, v.CurrentOdometer, v.IncludePurchaseInTotals,
        v.Notes, v.CoverPhotoId, Stats(v));

    public static string? Clean(this string? s, int max = 500)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();
        return s.Length > max ? s[..max] : s;
    }

    /// <summary>"#Wheel & Tyre " → "wheel_&_tyre"; lowercase, no '#', spaces → '_'.</summary>
    public static string? NormalizeTag(string raw)
    {
        var t = raw.Trim().TrimStart('#').Trim().ToLowerInvariant();
        t = string.Join('_', t.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries));
        if (t.Length == 0) return null;
        return t.Length > 50 ? t[..50] : t;
    }
}
