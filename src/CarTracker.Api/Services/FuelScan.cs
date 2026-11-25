using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ImageMagick;

namespace CarTracker.Api.Services;

/// <summary>What the AI reads from a pump display or fuel receipt photo.</summary>
public record FuelScan
{
    [JsonPropertyName("is_fuel")] public bool IsFuel { get; init; }
    /// <summary>pump_display | receipt | other</summary>
    [JsonPropertyName("source")] public string Source { get; init; } = "other";
    [JsonPropertyName("total")] public decimal? Total { get; init; }
    [JsonPropertyName("liters")] public decimal? Liters { get; init; }
    [JsonPropertyName("price_per_liter")] public decimal? PricePerLiter { get; init; }
    /// <summary>95 | 98 | 100 | 102 | diesel | null</summary>
    [JsonPropertyName("grade")] public string? Grade { get; init; }
    [JsonPropertyName("product_name")] public string? ProductName { get; init; }
    [JsonPropertyName("station")] public string? Station { get; init; }
    /// <summary>YYYY-MM-DD, receipts only.</summary>
    [JsonPropertyName("date")] public string? Date { get; init; }

    public static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
}

public record FuelScanResult(FuelScan? Data, string? Error, string? Model, int InputTokens, int OutputTokens, decimal EstimatedCostUsd, bool AiCalled);

public interface IFuelScanner
{
    /// <summary>Reads a (JPEG) photo of a pump display or receipt.</summary>
    Task<FuelScanResult> ScanAsync(byte[] jpeg, CancellationToken ct);
}

public static class FuelImage
{
    /// <summary>Upright, metadata-free JPEG, max 1600 px: digits stay readable and the request stays small.</summary>
    public static byte[] Prepare(byte[] bytes)
    {
        using var img = new MagickImage(bytes);
        img.AutoOrient();
        img.Strip();
        if (img.Width > 1600 || img.Height > 1600) img.Resize(new MagickGeometry(1600, 1600));
        img.Format = MagickFormat.Jpeg;
        img.Quality = 85;
        return img.ToByteArray();
    }
}

public class OpenAiFuelScanner(HttpClient http, AiOptions options, ILogger<OpenAiFuelScanner> log) : IFuelScanner
{
    private const string Instructions = """
        You read fuel purchases from a photo for a car owner's fuel log. The photo shows either a fuel pump display
        (mostly German stations) or a printed fuel receipt.

        Pump displays usually show three numbers: the amount to pay ("Betrag", "EUR", "€", "Zu zahlen"), the volume ("Liter", "Abgabe")
        and the price per litre ("Preis/Liter", "€/l", "Grundpreis"). They use seven-segment digits; decimal separators may be faint.
        - price_per_liter usually has three decimals, the last one often smaller/raised ("1,85⁹" → 1.859). Plausible range 0.80–3.50.
        - liters usually has two decimals, plausible range 1–150. total has two decimals.
        - Check total ≈ liters × price_per_liter and use that to resolve unclear digits or decimal positions.
        - If several hoses/products are shown, use the values of the one that was actually pumped (non-zero amount).
        - A value you cannot read with confidence → null. Never guess digits.
        Receipts: take the fuel line only (not car wash, shop items or deposits); total is what was paid for the fuel.

        Numbers use a dot as decimal separator in your output.
        grade: only from text visible in the photo:
          the octane number if printed (95, 98, 100, 102);
          "Super", "Super E5", "Super E10", "Benzin 95" → 95; "Super Plus" → 98;
          anything with "Diesel" → diesel;
          premium brand names without a visible number (e.g. "V-Power", "Ultimate", "Excellium") → null.
        product_name: the fuel product name as shown, else null.
        station: brand/operator if visible (Aral, Shell, Esso, Total, JET, …), else null.
        date: purchase date as YYYY-MM-DD if printed (receipts), else null.
        source: pump_display, receipt, or other.
        If the photo shows neither a pump display nor a fuel receipt, set is_fuel=false and everything else null.
        """;

    public async Task<FuelScanResult> ScanAsync(byte[] jpeg, CancellationToken ct)
    {
        var part = new JsonObject
        {
            ["type"] = "input_image", ["image_url"] = $"data:image/jpeg;base64,{Convert.ToBase64String(jpeg)}", ["detail"] = "high",
        };
        var raw = await OpenAiResponses.CallAsync(http, options, log, Instructions, part, "Read the fuel purchase.", "fuel", Schema(), ct);
        return FromRaw(raw);
    }

    /// <summary>Parses a Responses API result. Public for tests.</summary>
    public static FuelScanResult Parse(string responseJson, AiOptions options) => FromRaw(OpenAiResponses.Parse(responseJson, options));

    private static FuelScanResult FromRaw(AiCallResult r)
    {
        if (r.Text is null) return new(null, r.Error, r.Model, r.InputTokens, r.OutputTokens, r.EstimatedCostUsd, r.AiCalled);
        try
        {
            return new(JsonSerializer.Deserialize<FuelScan>(r.Text, FuelScan.Json), null, r.Model, r.InputTokens, r.OutputTokens, r.EstimatedCostUsd, true);
        }
        catch (JsonException)
        {
            return new(null, "The AI result couldn't be read.", r.Model, r.InputTokens, r.OutputTokens, r.EstimatedCostUsd, true);
        }
    }

    private static JsonObject Schema()
    {
        var str = OpenAiResponses.Str;
        var num = OpenAiResponses.Num;
        return new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new JsonArray("is_fuel", "source", "total", "liters", "price_per_liter", "grade", "product_name", "station", "date"),
            ["properties"] = new JsonObject
            {
                ["is_fuel"] = new JsonObject { ["type"] = "boolean" },
                ["source"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("pump_display", "receipt", "other") },
                ["total"] = num(true),
                ["liters"] = num(true),
                ["price_per_liter"] = num(true),
                ["grade"] = new JsonObject { ["type"] = new JsonArray("string", "null"), ["enum"] = new JsonArray("95", "98", "100", "102", "diesel", null) },
                ["product_name"] = str(true),
                ["station"] = str(true),
                ["date"] = str(true),
            },
        };
    }
}

/// <summary>Fixed pump reading for clicking through the UI without credits (Ai:Provider=Fake).</summary>
public class FakeFuelScanner : IFuelScanner
{
    public async Task<FuelScanResult> ScanAsync(byte[] jpeg, CancellationToken ct)
    {
        await Task.Delay(1200, ct);
        return new(new FuelScan
        {
            IsFuel = true, Source = "pump_display", Total = 87.41m, Liters = 46.52m, PricePerLiter = 1.879m,
            Grade = "98", ProductName = "Super Plus", Station = "Aral",
        }, null, "fake", 0, 0, 0, false);
    }
}
