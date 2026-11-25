using System.Text.Json;
using System.Text.Json.Nodes;

namespace CarTracker.Api.Services;

public class AiOptions
{
    /// <summary>"OpenAI" (default when a key is set), "Fake" (dev/UI testing without credits), or "None".</summary>
    public string Provider { get; set; } = "OpenAI";
    public string? ApiKey { get; set; }
    public string Model { get; set; } = "gpt-5-mini";
    /// <summary>For reasoning models (gpt-5 family): minimal | low | medium | high. Empty → not sent.</summary>
    public string? ReasoningEffort { get; set; } = "low";
    /// <summary>USD per 1M tokens, only used for the usage estimate shown to the admin.</summary>
    public decimal InputPricePerMTok { get; set; } = 0.25m;
    public decimal OutputPricePerMTok { get; set; } = 2.00m;
    /// <summary>Bill imports per user per calendar month (admins unlimited). 0 = unlimited.</summary>
    public int MonthlyLimitPerUser { get; set; } = 60;
    /// <summary>Fuel photo scans per user per calendar month (admins unlimited). 0 = unlimited. Much cheaper than bills.</summary>
    public int MonthlyFuelScansPerUser { get; set; } = 100;
}

/// <summary>Calls the OpenAI Responses API with the PDF/image and a strict JSON schema.</summary>
public class OpenAiBillExtractor(HttpClient http, AiOptions options, ILogger<OpenAiBillExtractor> log) : IBillExtractor
{
    private const string Instructions = """
        You extract data from car-related invoices, order confirmations and receipts (mostly German, sometimes English)
        for a private person's car expense tracker.

        Rules:
        - All money values are what the customer pays: GROSS, including VAT. If the bill lists net prices, convert using the VAT rate on the bill.
        - Use a dot as decimal separator in numbers ("1.234,56 €" → 1234.56).
        - One line_item per position on the bill. Keep the product name as written, but drop internal noise
          (SKU prefixes, "Art.-Nr." labels). Put manufacturer into brand and the manufacturer/OEM part number into part_number when present.
        - short_name: how a car owner would write the item in their logbook, in the bill's language: brand + what it is,
          ideally 2–5 words, max 40 characters. Drop fitment lists ("passend für BMW 1er F2x…"), vehicle models, engine codes,
          part numbers, sizes, quantities, pack sizes and marketing words (Special, Premium, Original, Set, Kit unless essential).
          Use normal capitalization (not ALL CAPS). Examples:
            "55Parts Special: Offene Ansaugung passend für BMW 1er F2x, 2er F2x, 3er F3x, 4er F3x, M2 F87 N55" → "55Parts Offene Ansaugung"
            "ZIMMERMANN Bremsscheibe Coat Z vorne belüftet 340x30mm BMW M2 F87" → "Zimmermann Bremsscheiben vorne"
            "Castrol EDGE 5W-30 LL 5 Liter Motoröl BMW Longlife-04" → "Castrol Edge 5W-30"
            "Versandkosten DHL Paket" → "Versand"
        - Shipping, payment fees, deposits (Pfand/Altteilpfand) and discounts are their own line_items with line_type
          shipping / fee / discount; discounts have NEGATIVE totals. Workshop work time is line_type labour.
        - Sum of all line_item totals should equal grand_total. Do not invent lines to make it match.
        - category: one or two lowercase words describing the car area, in the language given in the request (CATEGORY LANGUAGE).
          English examples: brakes, suspension, oil, engine, exhaust, wheels, tyres, lighting, interior, exterior, cooling,
          electrical, tools, cleaning. German examples: bremsen, fahrwerk, öl, motor, auspuff, felgen, reifen, beleuchtung,
          innenraum, karosserie, kühlung, elektrik, werkzeug, pflege. null if unclear.
        - purpose: your best guess what the item is for the car owner:
          upgrade = modification beyond stock (performance/tuning parts, coilovers, aftermarket wheels, styling, retrofits),
          repair = replacing worn/broken parts (brake discs, bushings, sensors, gaskets),
          service = routine maintenance and consumables (oil, filters, spark plugs, fluids, inspection),
          other = tools, cleaning products, accessories, shipping/fees/discounts.
        - invoice_date as YYYY-MM-DD (invoice/order date, not delivery date).
        - vehicle_hint: VIN, licence plate or vehicle model if the bill mentions one, else null.
        - odometer_km: mileage if stated (workshop bills), else null.
        - notes: anything important that doesn't fit elsewhere (e.g. warranty info, return deadline), max 2 sentences, else null.
        - If the document is not an invoice/receipt at all, set is_invoice=false and leave the rest empty/null.
        """;

    public async Task<ExtractionResult> ExtractAsync(byte[] content, string contentType, string fileName, string language, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
            return new(null, Loc.T("No OpenAI API key configured. Fill in the bill manually or set Ai:ApiKey.", "Kein OpenAI-Schlüssel eingerichtet. Füll die Rechnung von Hand aus oder setz Ai:ApiKey."), null, 0, 0, 0, false);

        var b64 = Convert.ToBase64String(content);
        JsonObject filePart = contentType == "application/pdf"
            ? new() { ["type"] = "input_file", ["filename"] = fileName, ["file_data"] = $"data:application/pdf;base64,{b64}" }
            : new() { ["type"] = "input_image", ["image_url"] = $"data:{contentType};base64,{b64}", ["detail"] = "high" };

        var prompt = $"Extract the bill. CATEGORY LANGUAGE: {(Loc.Normalize(language) == "de" ? "German" : "English")}.";
        var raw = await OpenAiResponses.CallAsync(http, options, log, Instructions, filePart, prompt, "bill", Schema(), ct);
        return FromRaw(raw);
    }

    /// <summary>Parses a Responses API result. Public for tests.</summary>
    public static ExtractionResult Parse(string responseJson, AiOptions options) => FromRaw(OpenAiResponses.Parse(responseJson, options));

    private static ExtractionResult FromRaw(AiCallResult r)
    {
        if (r.Text is null) return new(null, r.Error, r.Model, r.InputTokens, r.OutputTokens, r.EstimatedCostUsd, r.AiCalled);
        try
        {
            var data = JsonSerializer.Deserialize<BillExtraction>(r.Text, BillExtraction.Json);
            return new(data, null, r.Model, r.InputTokens, r.OutputTokens, r.EstimatedCostUsd, true);
        }
        catch (JsonException)
        {
            return new(null, Loc.T("The AI result couldn't be read.", "Das KI-Ergebnis war nicht lesbar."), r.Model, r.InputTokens, r.OutputTokens, r.EstimatedCostUsd, true);
        }
    }

    private static JsonObject Schema()
    {
        var Str = OpenAiResponses.Str;
        var Num = OpenAiResponses.Num;

        var line = new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new JsonArray("name", "short_name", "brand", "part_number", "quantity", "unit_price", "total", "line_type", "category", "purpose"),
            ["properties"] = new JsonObject
            {
                ["name"] = Str(false),
                ["short_name"] = Str(false),
                ["brand"] = Str(),
                ["part_number"] = Str(),
                ["quantity"] = Num(false),
                ["unit_price"] = Num(false),
                ["total"] = Num(false),
                ["line_type"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("part", "labour", "shipping", "discount", "fee", "other") },
                ["category"] = Str(),
                ["purpose"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("upgrade", "repair", "service", "other") },
            },
        };

        return new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new JsonArray("is_invoice", "vendor", "invoice_number", "invoice_date", "currency", "line_items",
                "grand_total", "vehicle_hint", "odometer_km", "notes"),
            ["properties"] = new JsonObject
            {
                ["is_invoice"] = new JsonObject { ["type"] = "boolean" },
                ["vendor"] = Str(),
                ["invoice_number"] = Str(),
                ["invoice_date"] = Str(),
                ["currency"] = Str(),
                ["line_items"] = new JsonObject { ["type"] = "array", ["items"] = line },
                ["grand_total"] = Num(),
                ["vehicle_hint"] = Str(),
                ["odometer_km"] = new JsonObject { ["type"] = new JsonArray("integer", "null") },
                ["notes"] = Str(),
            },
        };
    }
}

/// <summary>Returns a fixed sample bill. For clicking through the UI without spending credits (Ai:Provider=Fake).</summary>
public class FakeBillExtractor : IBillExtractor
{
    public async Task<ExtractionResult> ExtractAsync(byte[] content, string contentType, string fileName, string language, CancellationToken ct)
    {
        await Task.Delay(1500, ct); // feel the "processing" state
        var de = Loc.Normalize(language) == "de";
        var data = new BillExtraction
        {
            IsInvoice = true, Vendor = "Leebmann24 GmbH", InvoiceNumber = $"FAKE-{content.Length}", InvoiceDate = "2026-11-20",
            Currency = "EUR", GrandTotal = 612.37m, VehicleHint = null, OdometerKm = null,
            Notes = "Sample data from the fake extractor.",
            LineItems =
            [
                new() { Name = "Bremsscheibe vorne belüftet", ShortName = "Zimmermann Bremsscheiben vorne", Brand = "Zimmermann", PartNumber = "150.3471.20", Quantity = 2, UnitPrice = 189.90m, Total = 379.80m, LineType = "part", Category = de ? "bremsen" : "brakes", Purpose = "repair" },
                new() { Name = "Bremsbelagsatz vorne", ShortName = "Textar Bremsbeläge vorne", Brand = "Textar", PartNumber = "2508101", Quantity = 1, UnitPrice = 129.95m, Total = 129.95m, LineType = "part", Category = de ? "bremsen" : "brakes", Purpose = "repair" },
                new() { Name = "Verschleißanzeige vorne", ShortName = "Verschleißanzeige vorne", Brand = "BMW", PartNumber = "34356792289", Quantity = 1, UnitPrice = 14.73m, Total = 14.73m, LineType = "part", Category = de ? "bremsen" : "brakes", Purpose = "repair" },
                new() { Name = "Motoröl 5W-30 LL-04 5L", ShortName = "Castrol 5W-30", Brand = "Castrol", PartNumber = null, Quantity = 1, UnitPrice = 79.99m, Total = 79.99m, LineType = "part", Category = de ? "öl" : "oil", Purpose = "service" },
                new() { Name = "Versand DHL", ShortName = "Versand", Brand = null, PartNumber = null, Quantity = 1, UnitPrice = 7.90m, Total = 7.90m, LineType = "shipping", Category = null, Purpose = "other" },
            ],
        };
        return new(data, null, "fake", 0, 0, 0, false);
    }
}
