using System.Net;
using System.Net.Http.Json;
using CarTracker.Api.Data;
using CarTracker.Api.Endpoints;
using CarTracker.Api.Services;
using ImageMagick;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CarTracker.Tests;

public class StubFuelScanner : IFuelScanner
{
    public Task<FuelScanResult> ScanAsync(byte[] jpeg, CancellationToken ct) =>
        Task.FromResult(new FuelScanResult(new FuelScan
        {
            IsFuel = true, Source = "pump_display", Total = 87.41m, Liters = 46.52m, PricePerLiter = null, Grade = "98", Station = "Aral",
        }, null, "stub", 900, 80, 0.0004m, true));
}

public class FuelAppFactory : AppFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Ai:MonthlyFuelScansPerUser", "2");
        builder.ConfigureServices(s => s.Replace(ServiceDescriptor.Singleton<IFuelScanner, StubFuelScanner>()));
    }
}

public class FuelApiTests(FuelAppFactory factory) : IClassFixture<FuelAppFactory>
{
    private static VehicleRequest Car(string name) =>
        new(name, "BMW", null, null, null, null, null, null, null, null, null, null, null, false, null, null);

    private static FuelUpRequest Fill(string date, int? odo, decimal? total, decimal? liters = null, decimal? ppl = null, bool full = true, string? grade = "98") =>
        new(DateOnly.Parse(date), odo, grade, liters, ppl, total, full, null, null);

    private static MultipartFormDataContent Photo()
    {
        using var img = new MagickImage(MagickColors.White, 64, 32);
        var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(img.ToByteArray(MagickFormat.Png)), "file", "pump.png");
        return form;
    }

    [Fact]
    public async Task Fill_up_computes_litres_and_shows_in_vehicle_stats()
    {
        var c = await factory.CreateUserAsync("fuel@example.test");
        var car = (await (await c.PostAsJsonAsync("/api/vehicles", Car("M2"))).Content.ReadFromJsonAsync<VehicleDto>())!;

        var res = await c.PostAsJsonAsync($"/api/vehicles/{car.Id}/fuel", Fill("2026-11-01", 10_000, 90m, ppl: 1.80m));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var fill = (await res.Content.ReadFromJsonAsync<FuelUpDto>())!;
        Assert.Equal(50m, fill.Liters);

        await c.PostAsJsonAsync($"/api/vehicles/{car.Id}/fuel", Fill("2026-11-08", 10_300, 18m, liters: 10m, full: false));
        await c.PostAsJsonAsync($"/api/vehicles/{car.Id}/fuel", Fill("2026-11-15", 10_600, 54m, liters: 30m));

        var log = (await c.GetFromJsonAsync<FuelLogDto>($"/api/vehicles/{car.Id}/fuel"))!;
        Assert.Equal(3, log.FuelUps.Count);
        Assert.Equal(10_600, log.FuelUps[0].Odometer);            // newest first
        Assert.Equal(40m / 600m * 100, log.FuelUps[0].Consumption!.Value, 2); // 10 + 30 L over 600 km
        Assert.Equal("98", log.Stats.LastGrade);

        var v = (await c.GetFromJsonAsync<VehicleDto>($"/api/vehicles/{car.Id}"))!;
        Assert.Equal(162m, v.Stats.FuelTotal);
        Assert.Equal(162m, v.Stats.TotalSpent);
        Assert.Equal(10_600, v.Stats.LatestOdometer);
    }

    [Fact]
    public async Task Fill_ups_are_private_and_validated()
    {
        var c = await factory.CreateUserAsync("fuel-owner@example.test");
        var car = (await (await c.PostAsJsonAsync("/api/vehicles", Car("E46"))).Content.ReadFromJsonAsync<VehicleDto>())!;
        var fill = (await (await c.PostAsJsonAsync($"/api/vehicles/{car.Id}/fuel", Fill("2026-11-01", null, 50m))).Content.ReadFromJsonAsync<FuelUpDto>())!;

        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync($"/api/vehicles/{car.Id}/fuel", Fill("2026-11-01", null, null, liters: 40m))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync($"/api/vehicles/{car.Id}/fuel", Fill("2026-11-01", null, 50m, grade: "91"))).StatusCode);

        var other = await factory.CreateUserAsync("fuel-snoop@example.test");
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/vehicles/{car.Id}/fuel")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/fuel/{fill.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/fuel/{fill.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/fuel/{fill.Id}")).StatusCode);
    }

    [Fact]
    public async Task Scan_fills_in_missing_value_counts_usage_and_respects_limit()
    {
        var c = await factory.CreateUserAsync("fuel-scan@example.test");
        var res = await c.PostAsync("/api/fuel/scan", Photo());
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var scan = (await res.Content.ReadFromJsonAsync<FuelScanDto>())!;
        Assert.Equal(1.879m, scan.PricePerLiter);  // 87.41 / 46.52
        Assert.Equal("98", scan.Grade);
        Assert.Empty(scan.Warnings);

        await c.PostAsync("/api/fuel/scan", Photo());
        Assert.Equal((HttpStatusCode)429, (await c.PostAsync("/api/fuel/scan", Photo())).StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(2, db.AiUsages.Count(u => u.Purpose == "fuel" && u.Model == "stub"));
    }

    [Fact]
    public async Task Scan_rejects_non_images()
    {
        var c = await factory.CreateUserAsync("fuel-junk@example.test");
        var form = new MultipartFormDataContent { { new ByteArrayContent("hello"u8.ToArray()), "file", "x.jpg" } };
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsync("/api/fuel/scan", form)).StatusCode);
    }
}

public class FuelMathTests
{
    private static int _id;
    private static FuelUp F(string date, int? odo, decimal? liters, bool full = true, decimal total = 10m) =>
        new() { Id = ++_id, Date = DateOnly.Parse(date), Odometer = odo, Liters = liters, FullTank = full, Total = total };

    [Fact]
    public void Complete_fills_the_missing_value()
    {
        Assert.Equal(50m, FuelMath.Complete(90m, null, 1.8m).Liters);
        Assert.Equal(90m, FuelMath.Complete(null, 50m, 1.8m).Total);
        Assert.Equal(1.8m, FuelMath.Complete(90m, 50m, null).PricePerLiter);
        Assert.Null(FuelMath.Complete(90m, null, null).Liters);
    }

    [Fact]
    public void First_full_tank_only_anchors_and_partials_count_into_next_segment()
    {
        var segs = FuelMath.Segments([F("2026-01-01", 1000, 50), F("2026-01-05", 1200, 10, full: false), F("2026-01-10", 1500, 25)]);
        var s = Assert.Single(segs);
        Assert.Equal(500, s.DistanceKm);
        Assert.Equal(35m, s.Liters);
        Assert.Equal(7m, s.Consumption);
    }

    [Fact]
    public void Missing_litres_break_the_stretch_and_odometer_typos_are_flagged()
    {
        Assert.Empty(FuelMath.Segments([F("2026-01-01", 1000, 50), F("2026-01-05", null, null, full: false), F("2026-01-10", 1500, 25)]));

        var typo = FuelMath.Segments([F("2026-01-01", 1000, 50), F("2026-01-10", 1050, 45)]); // 90 L/100 km
        Assert.False(Assert.Single(typo).Plausible);
        Assert.Null(FuelMath.Compute([F("2026-01-01", 1000, 50), F("2026-01-10", 1050, 45)], new DateOnly(2026, 1, 20)).AvgConsumption);
    }

    [Fact]
    public void Scan_dto_warns_when_numbers_dont_add_up()
    {
        var dto = FuelEndpoints.ToScanDto(new FuelScan { IsFuel = true, Total = 100m, Liters = 40m, PricePerLiter = 1.899m, Grade = "super" });
        Assert.Contains(dto.Warnings, w => w.Contains("doesn't match"));
        Assert.Null(dto.Grade);
    }

    [Fact]
    public void Parses_fuel_scan_response()
    {
        const string response = """
        {"model":"gpt-5-mini","output":[{"type":"message","content":[{"type":"output_text","text":"{\"is_fuel\":true,\"source\":\"pump_display\",\"total\":61.2,\"liters\":32.5,\"price_per_liter\":1.883,\"grade\":null,\"product_name\":\"V-Power\",\"station\":\"Shell\",\"date\":null}"}]}],
         "usage":{"input_tokens":1500,"output_tokens":300}}
        """;
        var r = OpenAiFuelScanner.Parse(response, new AiOptions());
        Assert.Null(r.Error);
        Assert.Equal(32.5m, r.Data!.Liters);
        Assert.Null(r.Data.Grade);
        Assert.Equal("Shell", r.Data.Station);
    }
}
