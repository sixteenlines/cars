using System.Text.Json;
using System.Text.Json.Serialization;

namespace CarTracker.Api.Services;

/// <summary>What we ask the AI to read from a bill. Property names = JSON schema names (snake_case).</summary>
public record BillExtraction
{
    [JsonPropertyName("is_invoice")] public bool IsInvoice { get; init; }
    [JsonPropertyName("vendor")] public string? Vendor { get; init; }
    [JsonPropertyName("invoice_number")] public string? InvoiceNumber { get; init; }
    /// <summary>ISO date YYYY-MM-DD.</summary>
    [JsonPropertyName("invoice_date")] public string? InvoiceDate { get; init; }
    [JsonPropertyName("currency")] public string? Currency { get; init; }
    [JsonPropertyName("line_items")] public List<BillLine> LineItems { get; init; } = [];
    /// <summary>Amount actually paid, incl. VAT, shipping and discounts.</summary>
    [JsonPropertyName("grand_total")] public decimal? GrandTotal { get; init; }
    /// <summary>VIN, plate or model mentioned on the bill (workshop invoices usually have one).</summary>
    [JsonPropertyName("vehicle_hint")] public string? VehicleHint { get; init; }
    [JsonPropertyName("odometer_km")] public int? OdometerKm { get; init; }
    [JsonPropertyName("notes")] public string? Notes { get; init; }

    public static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
}

public record BillLine
{
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    /// <summary>Concise logbook name ("55Parts Offene Ansaugung"); used for entry titles. Null on old extractions.</summary>
    [JsonPropertyName("short_name")] public string? ShortName { get; init; }
    [JsonPropertyName("brand")] public string? Brand { get; init; }
    [JsonPropertyName("part_number")] public string? PartNumber { get; init; }
    [JsonPropertyName("quantity")] public decimal Quantity { get; init; } = 1;
    /// <summary>Gross unit price incl. VAT.</summary>
    [JsonPropertyName("unit_price")] public decimal UnitPrice { get; init; }
    /// <summary>Gross line total incl. VAT (negative for discounts).</summary>
    [JsonPropertyName("total")] public decimal Total { get; init; }
    /// <summary>part | labour | shipping | discount | fee | other</summary>
    [JsonPropertyName("line_type")] public string LineType { get; init; } = "part";
    /// <summary>Short category in the user's language ("brakes" / "bremsen") — becomes a tag suggestion.</summary>
    [JsonPropertyName("category")] public string? Category { get; init; }
    /// <summary>upgrade (→ Mod) | repair | service (→ Maintenance) | other (→ Expense). A guess the user can change.</summary>
    [JsonPropertyName("purpose")] public string Purpose { get; init; } = "repair";
}

public record ExtractionResult(
    BillExtraction? Data, string? Error, string? Model, int InputTokens, int OutputTokens, decimal EstimatedCostUsd, bool AiCalled);

public interface IBillExtractor
{
    /// <param name="content">PDF bytes, or a JPEG for photographed receipts.</param>
    /// <param name="language">"en" or "de": language for category (tag) suggestions.</param>
    Task<ExtractionResult> ExtractAsync(byte[] content, string contentType, string fileName, string language, CancellationToken ct);
}

public static class BillChecks
{
    /// <summary>Human-readable warnings shown on the review screen.</summary>
    public static List<string> Warnings(BillExtraction x)
    {
        var w = new List<string>();
        if (!x.IsInvoice) w.Add(Loc.T("This doesn't look like an invoice or receipt.", "Das sieht nicht nach einer Rechnung oder Quittung aus."));
        if (x.LineItems.Count == 0) w.Add(Loc.T("No line items found.", "Keine Positionen gefunden."));
        if (x.GrandTotal is { } total && x.LineItems.Count > 0)
        {
            var sum = x.LineItems.Sum(l => l.Total);
            if (Math.Abs(sum - total) > 0.05m)
                w.Add(Loc.T($"Line items add up to {Loc.Money(sum)} but the bill total is {Loc.Money(total)}. Please check.",
                    $"Die Positionen ergeben {Loc.Money(sum)}, die Rechnungssumme ist aber {Loc.Money(total)}. Bitte prüfen."));
        }
        if (x.Currency is { Length: > 0 } c && !c.Equals("EUR", StringComparison.OrdinalIgnoreCase))
            w.Add(Loc.T($"Bill currency is {c}; amounts are imported as-is.", $"Die Rechnung ist in {c}; die Beträge werden unverändert übernommen."));
        return w;
    }
}
