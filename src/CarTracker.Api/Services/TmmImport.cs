using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CarTracker.Api.Data;

namespace CarTracker.Api.Services;

// Track My Mods CSV export → vehicle, entries and fill-ups.
//
// One file per vehicle, sections each with their own header row:
//   Exported from Track My Mods on …
//   Vehicle Specifications / Groups / Modifications / Expenses / Events
// Money comes formatted ("1 400,00 €"), dates as "2026-01-31", "2026-01-31 18:38" or "2026-01-18T00:00:00.000",
// odometers as floats ("273000.0"). Events and Groups are ignored.
//
// Warnings are written in the request's language (Loc.T), they go straight to the preview screen.

public record TmmVehicle(
    string Name, string? Make, string? Model, int? Year, string? Badge, string? ColorExterior, string? ColorInterior,
    string? Plate, string? Vin, decimal? PurchasePrice, DateOnly? PurchaseDate, int? PurchaseOdometer, string? Notes);

public record TmmEntry(
    EntryKind Kind, EntryStatus Status, string Title, DateOnly? Date, DateOnly? EndDate, int? Odometer,
    decimal Cost, string? PartNumber, List<string> Tags, string? Notes);

public record TmmFuel(DateOnly Date, int? Odometer, decimal Total, decimal? Liters, string? Notes);

public class TmmFile
{
    public required TmmVehicle Vehicle { get; init; }
    public List<TmmEntry> Entries { get; } = [];
    public List<TmmFuel> FuelUps { get; } = [];
    public List<string> Warnings { get; } = [];
}

public class TmmFormatException(string message) : Exception(message);

public static partial class TmmImport
{
    private const decimal MilesToKm = 1.609344m;

    public static TmmFile Parse(string text)
    {
        var records = Csv.Read(text);
        if (records.Count == 0 || !records[0][0].StartsWith("Exported from Track My Mods", StringComparison.OrdinalIgnoreCase))
            throw new TmmFormatException(Loc.T("This isn't a Track My Mods export (CSV).", "Das ist kein Track-My-Mods-Export (CSV)."));

        // Split into sections: a one-field row names the section, the next row is its header.
        var sections = new Dictionary<string, List<Dictionary<string, string>>>(StringComparer.OrdinalIgnoreCase);
        List<Dictionary<string, string>>? current = null;
        string[]? header = null;
        foreach (var r in records.Skip(1))
        {
            if (r.All(string.IsNullOrWhiteSpace)) continue;
            if (r.Count(f => f.Length > 0) == 1 && r[0].Length > 0 && Sections.Contains(r[0].Trim()))
            {
                current = sections[r[0].Trim()] = [];
                header = null;
                continue;
            }
            if (current is null) continue;
            if (header is null) { header = r.Select(h => h.Trim().ToLowerInvariant()).ToArray(); continue; }
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < header.Length; i++) row[header[i]] = i < r.Count ? r[i].Trim() : "";
            current.Add(row);
        }

        if (!sections.TryGetValue("Vehicle Specifications", out var specs) || specs.Count == 0)
            throw new TmmFormatException(Loc.T("The export has no vehicle data.", "Im Export fehlen die Fahrzeugdaten."));

        var s = specs[0];
        var warnings = new List<string>();
        var miles = Get(s, "odometer_unit").StartsWith("mile", StringComparison.OrdinalIgnoreCase);
        if (miles) warnings.Add(Loc.T("Odometer readings were in miles and have been converted to km.", "Kilometerstände waren in Meilen und wurden in km umgerechnet."));

        var currencyWarned = false;
        decimal? Money(string raw)
        {
            if (raw.Length == 0) return null;
            if (!currencyWarned && Regex.IsMatch(raw, @"[$£¥₣]|CHF|USD|GBP"))
            {
                currencyWarned = true;
                warnings.Add(Loc.T($"Amounts aren't in euros ({raw}); they're imported as euros without conversion.",
                    $"Beträge sind nicht in Euro ({raw}); sie werden ohne Umrechnung als Euro übernommen."));
            }
            return ParseMoney(raw);
        }
        int? Odo(string raw) => ParseNumber(raw) is { } n && n > 0 ? (int)Math.Round(miles ? n * MilesToKm : n) : null;

        var vehicle = new TmmVehicle(
            Name: Clip(Get(s, "name"), 100) is { Length: > 0 } n ? n : Loc.T("Imported car", "Importiertes Auto"),
            Make: Opt(s, "make", 100), Model: Opt(s, "model", 100),
            Year: int.TryParse(Get(s, "year"), out var y) && y is > 1885 and < 2100 ? y : null,
            Badge: Opt(s, "badge", 100), ColorExterior: Opt(s, "colour_exterior", 100), ColorInterior: Opt(s, "colour_interior", 100),
            Plate: Opt(s, "registration_plate", 20), Vin: Opt(s, "vin", 17)?.ToUpperInvariant(),
            PurchasePrice: Money(Get(s, "cost_purchase")), PurchaseDate: ParseDate(Get(s, "date_purchase")),
            PurchaseOdometer: Odo(Get(s, "odometer_purchase")), Notes: Opt(s, "note", 4000));

        var file = new TmmFile { Vehicle = vehicle };

        // A reading below half the purchase odometer is almost certainly a typo ("27300" for "273000").
        int? CheckOdo(int? odo, string what)
        {
            if (odo is null || vehicle.PurchaseOdometer is not { } p || odo >= p / 2) return odo;
            warnings.Add(Loc.T($"“{what}”: odometer {odo:N0} km looks like a typo (the car had {p:N0} km when bought), left out.",
                $"„{what}“: km-Stand {odo.Value.ToString("N0", Loc.German)} sieht nach Tippfehler aus (beim Kauf {p.ToString("N0", Loc.German)} km), weggelassen."));
            return null;
        }

        foreach (var (section, isMod) in new[] { ("Modifications", true), ("Expenses", false) })
        {
            if (!sections.TryGetValue(section, out var rows)) continue;
            foreach (var r in rows)
            {
                var group = Get(r, "group").ToLowerInvariant();
                var name = Clip(Get(r, "name"), 200);
                var cost = Money(Get(r, "cost")) ?? 0;
                var date = ParseDate(Get(r, "date"));
                var notes = JoinNotes(Get(r, "note"), Get(r, "links"));

                if (!isMod && group == "fuel")
                {
                    var label = name.Length > 0 ? name : Loc.T("Fill-up", "Tanken") + (date is { } fd ? " " + Loc.Date(fd) : "");
                    if (date is null)
                    {
                        warnings.Add(Loc.T($"Fill-up over {Loc.Money(cost)} has no date, skipped.", $"Tanken über {Loc.Money(cost)} hat kein Datum, übersprungen."));
                        continue;
                    }
                    // TMM has no litres field; some people type them into the reference number. Accept that if the price fits.
                    decimal? liters = ParseNumber(Get(r, "reference_number")) is { } l && l > 0 && cost > 0 && cost / l is >= 0.8m and <= 3.5m ? l : null;
                    file.FuelUps.Add(new TmmFuel(date.Value, CheckOdo(Odo(Get(r, "odometer")), label), cost, liters,
                        JoinNotes(name, notes)));
                    continue;
                }

                var (kind, tag) = isMod ? (EntryKind.Mod, ModTag(group)) : ExpenseKind(group);
                var title = name.Length > 0 ? name : GroupLabel(group);
                var tags = new List<string>();
                if (tag is not null) tags.Add(tag);
                tags.AddRange(Get(r, "tags").Split([',', ';', '#', ' '], StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim()));
                var completed = !Get(r, "completed").Equals("false", StringComparison.OrdinalIgnoreCase);
                var endDate = ParseDate(Get(r, "end_date"));

                file.Entries.Add(new TmmEntry(kind, completed ? EntryStatus.Done : EntryStatus.Planned, title, date,
                    endDate is { } e && date is { } d && e >= d ? e : null, CheckOdo(Odo(Get(r, "odometer")), title),
                    cost, Opt(r, "reference_number", 100), tags, notes));
            }
        }

        file.Warnings.AddRange(warnings);
        return file;
    }

    // ---- Groups → kinds and tags (tags in the user's language, like the AI bill categories) ---------------------

    private static string? ModTag(string group) => group switch
    {
        "engine" => Loc.T("engine", "motor"),
        "exhaust" => Loc.T("exhaust", "auspuff"),
        "brake" => Loc.T("brakes", "bremsen"),
        "wheel_tyre" => Loc.T("wheels", "räder"),
        "drivetrain" => Loc.T("drivetrain", "antrieb"),
        "suspension" => Loc.T("suspension", "fahrwerk"),
        "electronic_audio" => Loc.T("electrical", "elektrik"),
        "exterior" => Loc.T("exterior", "karosserie"),
        "interior" => Loc.T("interior", "innenraum"),
        "" or "other" => null,
        _ => group,
    };

    private static (EntryKind, string?) ExpenseKind(string group) => group switch
    {
        "service" or "repair" => (EntryKind.Maintenance, null),
        "insurance" => (EntryKind.Expense, Loc.T("insurance", "versicherung")),
        "registration" => (EntryKind.Expense, Loc.T("registration", "zulassung")),
        "inspection" => (EntryKind.Expense, Loc.T("inspection", "tüv")),
        "vehicle_membership" => (EntryKind.Expense, Loc.T("membership", "mitgliedschaft")),
        "" or "other" => (EntryKind.Expense, null),
        _ => (EntryKind.Expense, group),
    };

    private static string GroupLabel(string group) => group switch
    {
        "service" => Loc.T("Service", "Wartung"),
        "repair" => Loc.T("Repair", "Reparatur"),
        "insurance" => Loc.T("Insurance", "Versicherung"),
        "registration" => Loc.T("Registration", "Zulassung"),
        "inspection" => Loc.T("Inspection", "TÜV"),
        "vehicle_membership" => Loc.T("Membership", "Mitgliedschaft"),
        _ => Loc.T("Entry", "Eintrag"),
    };

    // ---- Small parsers ------------------------------------------------------------------------------------------

    private static readonly HashSet<string> Sections =
        new(["Vehicle Specifications", "Groups", "Modifications", "Expenses", "Events"], StringComparer.OrdinalIgnoreCase);

    private static string Get(Dictionary<string, string> row, string key) => row.TryGetValue(key, out var v) ? v : "";
    private static string Clip(string s, int max) => s.Length > max ? s[..max] : s;
    private static string? Opt(Dictionary<string, string> row, string key, int max) => Get(row, key) is { Length: > 0 } v ? Clip(v, max) : null;

    private static string? JoinNotes(params string?[] parts)
    {
        var text = string.Join("\n", parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()));
        return text.Length == 0 ? null : Clip(text, 4000);
    }

    public static DateOnly? ParseDate(string raw) =>
        raw.Length >= 10 && DateOnly.TryParseExact(raw[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    /// <summary>Plain number as TMM writes odometers and references: "273000.0", "58", "46,5".</summary>
    public static decimal? ParseNumber(string raw)
    {
        var s = raw.Trim().Replace(',', '.');
        return decimal.TryParse(s, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n) ? n : null;
    }

    /// <summary>"1 400,00 €", "40,00 €", "$1,234.56", "1.400" → number. Null if there's no number in it.</summary>
    public static decimal? ParseMoney(string raw)
    {
        var s = NonNumber().Replace(raw, "");
        if (s.Length == 0 || s == "-") return null;
        int comma = s.LastIndexOf(','), dot = s.LastIndexOf('.');
        if (comma >= 0 && dot >= 0)
            s = comma > dot ? s.Replace(".", "").Replace(',', '.') : s.Replace(",", "");
        else if (comma >= 0)
            s = ThousandsComma().IsMatch(s) ? s.Replace(",", "") : s.Replace(',', '.');
        else if (dot >= 0 && ThousandsDot().IsMatch(s))
            s = s.Replace(".", "");
        return decimal.TryParse(s, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n)
            ? Math.Round(n, 2) : null;
    }

    [GeneratedRegex(@"[^\d,.\-]")] private static partial Regex NonNumber();
    [GeneratedRegex(@"^-?\d{1,3}(,\d{3})+$")] private static partial Regex ThousandsComma();
    [GeneratedRegex(@"^-?\d{1,3}(\.\d{3})+$")] private static partial Regex ThousandsDot();

    /// <summary>RFC 4180-ish CSV: quoted fields may contain commas, quotes ("") and line breaks.</summary>
    private static class Csv
    {
        public static List<List<string>> Read(string text)
        {
            var rows = new List<List<string>>();
            var row = new List<string>();
            var field = new StringBuilder();
            var quoted = false;
            text = text.TrimStart('﻿');
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (quoted)
                {
                    if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else if (c == '"') quoted = false;
                    else field.Append(c);
                }
                else if (c == '"') quoted = true;
                else if (c == ',') { row.Add(field.ToString()); field.Clear(); }
                else if (c is '\n' or '\r')
                {
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                    row.Add(field.ToString()); field.Clear();
                    rows.Add(row); row = [];
                }
                else field.Append(c);
            }
            if (field.Length > 0 || row.Count > 0) { row.Add(field.ToString()); rows.Add(row); }
            return rows;
        }
    }
}
