using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using CarTracker.Api.Data;
using CarTracker.Api.Endpoints;
using CarTracker.Api.Services;

namespace CarTracker.Tests;

public class TmmImportTests(AppFactory factory) : IClassFixture<AppFactory>
{
    // Made-up export with the quirks of real ones: formatted money, three date styles, float odometers,
    // an odometer typo, fuel without a name (litres typed into the reference number), quoted fields, a planned mod.
    private const string Export = """
        Exported from Track My Mods on 2026-11-27 at 16:08

        Vehicle Specifications
        name,make,model,year,series,badge,colour_exterior,colour_interior,registration_plate,vin,fuel_unit,odometer_unit,cost_purchase,date_purchase,odometer_purchase,note
        Testwagen,BMW,323i,1999,3er,,schwarz,,AB-CD123,TESTVIN0000000002,litres,kilometers,"1 400,00 €",2026-01-18T00:00:00.000,272400.0,

        Groups
        type,group,note
        modification,engine,
        expense,fuel,

        Modifications
        group,name,completed,cost,created_at,date,end_date,odometer,end_odometer,location,end_location,person,reference_number,tags,links,note
        suspension,TA Technix 🤪,true,"250,00 €",2026-02-07 15:01,2026-02-07,,,,,,,,,,
        exterior,Heckspoilerlippe,false,"1 150,00 €",2025-10-02 11:00,2025-10-04,,,,,,,,,,
        exterior,Zierstäbe schwarz,true,"166,00 €",2025-07-14 11:33,2025-07-14,,,,,,,,,,"51712407179, 51712407180"

        Expenses
        group,name,completed,cost,created_at,date,end_date,odometer,end_odometer,location,end_location,person,reference_number,tags,links,note
        insurance,Haftpflicht 01-10,true,"324,00 €",2026-01-19 13:16,2026-01-19,2026-10-31,,,,,,,,,
        fuel,,true,"104,30 €",2026-01-31 18:38,2026-01-31,,273400.0,,,,,58,,,
        fuel,,true,"67,68 €",2026-01-19 13:17,2026-01-19,,,,,,,,,,
        service,"Öl-, Pollen-, Luftfilter",true,"20,00 €",2026-01-21 12:06,2026-01-21,,27300.0,,,,,,,,
        repair,Seitenblinker weiß,true,"41,26 €",2026-07-29 14:07,2026-07-29,,,,,,,Diederichs 1243070/1,,,
        service,Bremse hinten,true,"700,00 €",2025-09-30 15:28,2025-10-04,,57000.0,,,,,,,https://example.com/x,

        Events
        group,name,completed,cost,created_at,date,end_date,odometer,end_odometer,location,end_location,person,reference_number,tags,links,note
        """;

    [Theory]
    [InlineData("1 400,00 €", 140000)]
    [InlineData("1\u00A0400,00\u00A0€", 140000)]
    [InlineData("40,00 €", 4000)]
    [InlineData("$1,234.56", 123456)]
    [InlineData("1.400", 140000)]
    [InlineData("0,00 €", 0)]
    public void Parses_money(string raw, int expectedCents) => Assert.Equal(expectedCents / 100m, TmmImport.ParseMoney(raw));

    [Fact]
    public void Parses_export()
    {
        var f = TmmImport.Parse(Export);
        Assert.Equal("Testwagen", f.Vehicle.Name);
        Assert.Equal(1400m, f.Vehicle.PurchasePrice);
        Assert.Equal(new DateOnly(2026, 1, 18), f.Vehicle.PurchaseDate);
        Assert.Equal(272400, f.Vehicle.PurchaseOdometer);

        Assert.Equal(3, f.Entries.Count(e => e.Kind == EntryKind.Mod));
        var spoiler = f.Entries.Single(e => e.Title == "Heckspoilerlippe");
        Assert.Equal(EntryStatus.Planned, spoiler.Status);
        Assert.Equal(1150m, spoiler.Cost);
        Assert.Equal("51712407179, 51712407180", f.Entries.Single(e => e.Title == "Zierstäbe schwarz").Notes);

        var insurance = f.Entries.Single(e => e.Title == "Haftpflicht 01-10");
        Assert.Equal(EntryKind.Expense, insurance.Kind);
        Assert.Equal(new DateOnly(2026, 10, 31), insurance.EndDate);
        Assert.Equal("Diederichs 1243070/1", f.Entries.Single(e => e.Title == "Seitenblinker weiß").PartNumber);
        Assert.Equal(EntryKind.Maintenance, f.Entries.Single(e => e.Title == "Öl-, Pollen-, Luftfilter").Kind);

        Assert.Equal(2, f.FuelUps.Count);
        var fill = f.FuelUps.Single(x => x.Total == 104.30m);
        Assert.Equal(58m, fill.Liters);
        Assert.Equal(273400, fill.Odometer);
        Assert.Null(f.FuelUps.Single(x => x.Total == 67.68m).Liters);

        // 27300 next to a 272400 purchase odometer → dropped with a warning.
        Assert.Null(f.Entries.Single(e => e.Title == "Öl-, Pollen-, Luftfilter").Odometer);
        Assert.Contains(f.Warnings, w => w.Contains("typo"));
    }

    [Fact]
    public void Rejects_other_csv() =>
        Assert.Throws<TmmFormatException>(() => TmmImport.Parse("a,b,c\n1,2,3\n"));

    private static MultipartFormDataContent Form(string csv, int? vehicleId = null)
    {
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        content.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        var form = new MultipartFormDataContent { { content, "file", "Testwagen.csv" } };
        if (vehicleId is { } id) form.Add(new StringContent(id.ToString()), "vehicleId");
        return form;
    }

    [Fact]
    public async Task Preview_import_and_reimport_skips_duplicates()
    {
        var client = await factory.CreateUserAsync("tmm@example.test");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("de");

        var preview = await (await client.PostAsync("/api/import/tmm/preview", Form(Export))).Content.ReadFromJsonAsync<TmmPreviewDto>();
        Assert.Null(preview!.MatchVehicleId);
        Assert.Equal(3, preview.Mods);
        Assert.Equal(2, preview.FuelUps);
        Assert.Equal(1, preview.Planned);
        Assert.Contains(preview.Warnings, w => w.Contains("Tippfehler"));

        var res = await client.PostAsync("/api/import/tmm", Form(Export));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var result = await res.Content.ReadFromJsonAsync<TmmResultDto>();
        Assert.Equal(7, result!.Entries);   // 3 mods + 4 expenses
        Assert.Equal(2, result.FuelUps);

        var entries = await client.GetFromJsonAsync<List<EntryDto>>($"/api/vehicles/{result.VehicleId}/entries");
        Assert.Contains(entries!, e => e.Title == "TA Technix 🤪" && e.Tags.Contains("fahrwerk"));
        Assert.Contains(entries!, e => e.Title == "Haftpflicht 01-10" && e.Tags.Contains("versicherung"));
        var blinker = entries!.Single(e => e.Title == "Seitenblinker weiß");
        Assert.Equal(41.26m, blinker.Total);
        Assert.Equal("Diederichs 1243070/1", blinker.LineItems.Single().PartNumber);

        var car = await client.GetFromJsonAsync<VehicleDto>($"/api/vehicles/{result.VehicleId}");
        Assert.Equal("TESTVIN0000000002", car!.Vin);
        Assert.Equal(1400m, car.PurchasePrice);

        // Same file again: matched by VIN, everything is a duplicate.
        var again = await (await client.PostAsync("/api/import/tmm/preview", Form(Export))).Content.ReadFromJsonAsync<TmmPreviewDto>();
        Assert.Equal(result.VehicleId, again!.MatchVehicleId);
        Assert.Equal(9, again.Duplicates);
        var second = await (await client.PostAsync("/api/import/tmm", Form(Export, result.VehicleId))).Content.ReadFromJsonAsync<TmmResultDto>();
        Assert.Equal(0, second!.Entries + second.FuelUps);
        Assert.Equal(9, second.Skipped);
    }

    [Fact]
    public async Task Cannot_import_into_someone_elses_car()
    {
        var owner = await factory.CreateUserAsync("tmm-owner@example.test");
        var car = await (await owner.PostAsJsonAsync("/api/vehicles",
            new VehicleRequest("Mine", null, null, null, null, null, null, null, null, null, null, null, null, false, null, null)))
            .Content.ReadFromJsonAsync<VehicleDto>();
        var other = await factory.CreateUserAsync("tmm-other@example.test");
        var res = await other.PostAsync("/api/import/tmm", Form(Export, car!.Id));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }
}
