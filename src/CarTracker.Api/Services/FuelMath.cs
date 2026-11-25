using CarTracker.Api.Data;

namespace CarTracker.Api.Services;

public static class FuelMath
{
    public static readonly string[] Grades = ["95", "98", "100", "102", "diesel"];

    /// <summary>Given any two of total / litres / price per litre, fills in the third (rounded like a pump does).</summary>
    public static (decimal? Total, decimal? Liters, decimal? PricePerLiter) Complete(decimal? total, decimal? liters, decimal? ppl)
    {
        if (total is null && liters is > 0 && ppl is > 0) total = Math.Round(liters.Value * ppl.Value, 2);
        else if (liters is null && total is > 0 && ppl is > 0) liters = Math.Round(total.Value / ppl.Value, 2);
        else if (ppl is null && total is > 0 && liters is > 0) ppl = Math.Round(total.Value / liters.Value, 3);
        return (total, liters, ppl);
    }

    /// <summary>Chronological order used for consumption: date, then odometer, then insertion.</summary>
    public static List<FuelUp> Chronological(IEnumerable<FuelUp> fills) =>
        fills.OrderBy(f => f.Date).ThenBy(f => f.Odometer ?? int.MaxValue).ThenBy(f => f.Id).ToList();

    /// <summary>One full-to-full stretch, attributed to the fill-up that closed it.</summary>
    public record Segment(int FuelUpId, int DistanceKm, decimal Liters, decimal Cost)
    {
        public decimal Consumption => Math.Round(Liters / DistanceKm * 100, 2);
        /// <summary>Outside 2–40 L/100 km almost always means an odometer typo or a missed fill-up.</summary>
        public bool Plausible => Consumption is >= 2 and <= 40;
    }

    /// <summary>
    /// Full-to-full method: between two full tanks with odometer readings, everything poured in
    /// (incl. partial fills in between) was burned over that distance. A fill-up without litres breaks the stretch.
    /// </summary>
    public static List<Segment> Segments(IEnumerable<FuelUp> fills)
    {
        var result = new List<Segment>();
        FuelUp? anchor = null;
        decimal liters = 0, cost = 0;
        var broken = false;
        foreach (var f in Chronological(fills))
        {
            if (anchor is not null)
            {
                if (f.Liters is null) broken = true;
                liters += f.Liters ?? 0;
                cost += f.Total;
            }
            if (!f.FullTank || f.Odometer is null) continue;

            if (anchor is not null && !broken && f.Odometer > anchor.Odometer && liters > 0)
                result.Add(new Segment(f.Id, f.Odometer.Value - anchor.Odometer!.Value, liters, cost));
            anchor = f;
            liters = 0;
            cost = 0;
            broken = false;
        }
        return result;
    }

    public record Stats(
        int Count, decimal TotalCost, decimal TotalLiters, decimal? AvgConsumption, decimal? CostPer100Km, int DistanceKm,
        decimal ThisMonthCost, decimal ThisYearCost, string? LastGrade, DateOnly? LastDate, int? LastOdometer,
        Dictionary<string, decimal> AvgPricePerLiter);

    public static Stats Compute(IReadOnlyCollection<FuelUp> fills, DateOnly today)
    {
        var ordered = Chronological(fills);
        var good = Segments(ordered).Where(s => s.Plausible).ToList();
        var dist = good.Sum(s => s.DistanceKm);
        var segLiters = good.Sum(s => s.Liters);
        var segCost = good.Sum(s => s.Cost);
        var last = ordered.LastOrDefault();

        var ppl = fills.Where(f => f.Grade is not null && f.Liters is > 0)
            .GroupBy(f => f.Grade!)
            .ToDictionary(g => g.Key, g => Math.Round(g.Sum(f => f.Total) / g.Sum(f => f.Liters!.Value), 3));

        return new Stats(
            Count: fills.Count,
            TotalCost: fills.Sum(f => f.Total),
            TotalLiters: fills.Sum(f => f.Liters ?? 0),
            AvgConsumption: dist > 0 ? Math.Round(segLiters / dist * 100, 2) : null,
            CostPer100Km: dist > 0 ? Math.Round(segCost / dist * 100, 2) : null,
            DistanceKm: dist,
            ThisMonthCost: fills.Where(f => f.Date.Year == today.Year && f.Date.Month == today.Month).Sum(f => f.Total),
            ThisYearCost: fills.Where(f => f.Date.Year == today.Year).Sum(f => f.Total),
            LastGrade: ordered.LastOrDefault(f => f.Grade is not null)?.Grade,
            LastDate: last?.Date,
            LastOdometer: fills.Where(f => f.Odometer.HasValue).Select(f => f.Odometer).Max(),
            AvgPricePerLiter: ppl);
    }
}
