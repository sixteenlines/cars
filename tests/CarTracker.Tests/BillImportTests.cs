using System.Net;
using System.Net.Http.Json;
using System.Text;
using CarTracker.Api.Data;
using CarTracker.Api.Endpoints;
using CarTracker.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CarTracker.Tests;

/// <summary>Instant, deterministic extractor so tests never call OpenAI.</summary>
public class StubExtractor : IBillExtractor
{
    public Task<ExtractionResult> ExtractAsync(byte[] content, string contentType, string fileName, CancellationToken ct) =>
        Task.FromResult(new ExtractionResult(new BillExtraction
        {
            IsInvoice = true, Vendor = "Teileshop", InvoiceNumber = "R-1001", InvoiceDate = "2026-11-20", Currency = "EUR",
            GrandTotal = 107.90m, VehicleHint = "TESTVIN0000000001",
            LineItems =
            [
                new() { Name = "Bremsbelag", Quantity = 1, UnitPrice = 100m, Total = 100m, LineType = "part", Category = "brakes" },
                new() { Name = "Versand", Quantity = 1, UnitPrice = 7.90m, Total = 7.90m, LineType = "shipping" },
            ],
        }, null, "stub", 1000, 200, 0.001m, true));
}

public class BillAppFactory : AppFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Ai:MonthlyLimitPerUser", "2");
        builder.ConfigureServices(s => s.Replace(ServiceDescriptor.Singleton<IBillExtractor, StubExtractor>()));
    }
}

public class BillImportTests(BillAppFactory factory) : IClassFixture<BillAppFactory>
{
    private static readonly byte[] FakePdf = Encoding.ASCII.GetBytes("%PDF-1.4\n% test bill\n");

    private static MultipartFormDataContent Upload(byte[] bytes, string name = "bill.pdf")
    {
        var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(bytes), "file", name);
        return form;
    }

    private static async Task<DocumentDto> WaitUntilProcessed(HttpClient c, int id)
    {
        for (var i = 0; i < 50; i++)
        {
            var doc = await c.GetFromJsonAsync<DocumentDto>($"/api/documents/{id}");
            if (doc!.Status != DocumentStatus.Processing) return doc;
            await Task.Delay(100);
        }
        throw new TimeoutException("Document was not processed");
    }

    private static VehicleRequest Car(string name, string? vin = null) =>
        new(name, "BMW", null, null, null, null, null, null, vin, null, null, null, null, false, null, null);

    [Fact]
    public async Task Upload_extract_review_import_creates_linked_entries()
    {
        var c = await factory.CreateUserAsync("bills@example.test");
        await c.PostAsJsonAsync("/api/vehicles", Car("E46"));
        var m2 = await (await c.PostAsJsonAsync("/api/vehicles", Car("M2", "TESTVIN0000000001"))).Content.ReadFromJsonAsync<VehicleDto>();

        var res = await c.PostAsync("/api/documents", Upload(FakePdf));
        Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);
        var doc = await WaitUntilProcessed(c, (await res.Content.ReadFromJsonAsync<DocumentDto>())!.Id);

        Assert.Equal(DocumentStatus.Ready, doc.Status);
        Assert.Equal(m2!.Id, doc.VehicleId);              // matched via VIN on the bill
        Assert.Equal("Teileshop", doc.Extraction!.Vendor);
        Assert.Empty(doc.Warnings);                         // 100 + 7.90 = 107.90

        var entry = new EntryRequest(EntryKind.Maintenance, EntryStatus.Done, "Bremsbeläge", new DateOnly(2026, 11, 20), null, null,
            "Teileshop", null, null, null, ["brakes"], [new LineItemRequest("Bremsbelag", null, null, 1, 100m), new LineItemRequest("Versand", null, null, 1, 7.90m)]);
        var imported = await c.PostAsJsonAsync($"/api/documents/{doc.Id}/import", new ImportRequest([new ImportEntry(m2.Id, entry)]));
        Assert.Equal(HttpStatusCode.OK, imported.StatusCode);
        var refs = await imported.Content.ReadFromJsonAsync<List<EntryRef>>();

        var saved = await c.GetFromJsonAsync<EntryDto>($"/api/entries/{refs![0].Id}");
        Assert.Equal(107.90m, saved!.Total);
        Assert.Equal(doc.Id, Assert.Single(saved.Documents).Id);

        var after = await c.GetFromJsonAsync<DocumentDto>($"/api/documents/{doc.Id}");
        Assert.Equal(DocumentStatus.Imported, after!.Status);
        Assert.Single(after.Entries);

        var pdf = await c.GetAsync($"/api/documents/{doc.Id}/file");
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Same_file_twice_is_not_extracted_again_and_other_users_cant_see_it()
    {
        var c = await factory.CreateUserAsync("dupe@example.test");
        var bytes = Encoding.ASCII.GetBytes("%PDF-1.4\n% dupe\n");
        var first = await (await c.PostAsync("/api/documents", Upload(bytes))).Content.ReadFromJsonAsync<DocumentDto>();
        var second = await c.PostAsync("/api/documents", Upload(bytes, "renamed.pdf"));
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var dto = await second.Content.ReadFromJsonAsync<DocumentDto>();
        Assert.True(dto!.AlreadyUploaded);
        Assert.Equal(first!.Id, dto.Id);

        var other = await factory.CreateUserAsync("snoop@example.test");
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/documents/{first.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/documents/{first.Id}/file")).StatusCode);
    }

    [Fact]
    public async Task Discarded_bill_can_be_uploaded_again()
    {
        var c = await factory.CreateUserAsync("discard@example.test");
        var bytes = Encoding.ASCII.GetBytes("%PDF-1.4\n% discard\n");
        var first = await (await c.PostAsync("/api/documents", Upload(bytes))).Content.ReadFromJsonAsync<DocumentDto>();
        await WaitUntilProcessed(c, first!.Id);

        var del = await c.DeleteAsync($"/api/documents/{first.Id}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/documents/{first.Id}")).StatusCode);

        var again = await c.PostAsync("/api/documents", Upload(bytes));
        Assert.Equal(HttpStatusCode.Accepted, again.StatusCode);
    }

    [Fact]
    public async Task Monthly_limit_applies_to_non_admins()
    {
        var c = await factory.CreateUserAsync("limit@example.test");
        for (var i = 0; i < 2; i++)
        {
            var r = await c.PostAsync("/api/documents", Upload(Encoding.ASCII.GetBytes($"%PDF-1.4\n% limit {i}\n")));
            await WaitUntilProcessed(c, (await r.Content.ReadFromJsonAsync<DocumentDto>())!.Id);
        }
        var third = await c.PostAsync("/api/documents", Upload(Encoding.ASCII.GetBytes("%PDF-1.4\n% limit 3\n")));
        Assert.Equal((HttpStatusCode)429, third.StatusCode);
    }

    [Fact]
    public async Task Rejects_files_that_are_neither_pdf_nor_image()
    {
        var c = await factory.CreateUserAsync("junk@example.test");
        var res = await c.PostAsync("/api/documents", Upload(Encoding.ASCII.GetBytes("hello"), "bill.jpg"));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }
}

public class ExtractionParsingTests
{
    [Fact]
    public void Parses_responses_api_output_and_estimates_cost()
    {
        const string response = """
        {"model":"gpt-5-mini-2025-08-07","output":[
          {"type":"reasoning","summary":[]},
          {"type":"message","content":[{"type":"output_text","text":"{\"is_invoice\":true,\"vendor\":\"Shop\",\"invoice_number\":\"1\",\"invoice_date\":\"2026-01-02\",\"currency\":\"EUR\",\"line_items\":[{\"name\":\"Castrol EDGE 5W-30 LL 5 Liter Motoröl BMW Longlife-04\",\"short_name\":\"Castrol Edge 5W-30\",\"brand\":null,\"part_number\":null,\"quantity\":2,\"unit_price\":10.5,\"total\":21,\"line_type\":\"part\",\"category\":\"oil\"}],\"grand_total\":21,\"vehicle_hint\":null,\"odometer_km\":123456,\"notes\":null}"}]}],
         "usage":{"input_tokens":2000000,"output_tokens":1000000}}
        """;
        var r = OpenAiBillExtractor.Parse(response, new AiOptions { InputPricePerMTok = 0.25m, OutputPricePerMTok = 2m });
        Assert.Null(r.Error);
        Assert.Equal("Shop", r.Data!.Vendor);
        Assert.Equal("Castrol Edge 5W-30", r.Data.LineItems[0].ShortName);
        Assert.Equal(21m, r.Data.LineItems[0].Total);
        Assert.Equal(123456, r.Data.OdometerKm);
        Assert.Equal(2.5m, r.EstimatedCostUsd);
    }

    [Fact]
    public void Refusal_becomes_error()
    {
        const string response = """{"output":[{"type":"message","content":[{"type":"refusal","refusal":"nope"}]}],"usage":{"input_tokens":1,"output_tokens":1}}""";
        var r = OpenAiBillExtractor.Parse(response, new AiOptions());
        Assert.Null(r.Data);
        Assert.Contains("nope", r.Error);
    }

    [Fact]
    public void Warns_when_lines_dont_add_up()
    {
        var x = new BillExtraction { IsInvoice = true, GrandTotal = 50m, LineItems = [new() { Name = "a", Total = 40m }] };
        Assert.Contains(BillChecks.Warnings(x), w => w.Contains("add up"));
    }
}
