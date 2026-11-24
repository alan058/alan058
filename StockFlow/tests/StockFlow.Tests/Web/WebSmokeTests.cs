using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using StockFlow.Core.Reporting;

namespace StockFlow.Tests.Web;

/// <summary>
/// Hosts the real web app (in-memory data) and checks that every page renders.
/// Cheap regression protection against broken views, routes and DI wiring.
/// </summary>
public sealed partial class WebSmokeTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public WebSmokeTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Theory]
    [InlineData("/", "Dashboard")]
    [InlineData("/Products", "AUD-001")]
    [InlineData("/Products?q=speaker", "Bluetooth Speaker Mini")]
    [InlineData("/Products?lowStock=true", "CMP-003")]
    [InlineData("/Products/Details/1", "Stock by warehouse")]
    [InlineData("/Products/Create", "Add product")]
    [InlineData("/Products/Edit/1", "Edit AUD-001")]
    [InlineData("/Products/Receive/1", "Receive stock")]
    [InlineData("/Products/Adjust/1", "Adjust stock")]
    [InlineData("/Orders", "SO-000001")]
    [InlineData("/Orders?status=Allocated", "SO-000001")]
    [InlineData("/Orders/Details/5", "short in JHB")]
    [InlineData("/Orders/Create", "New sales order")]
    [InlineData("/Reports/LowStock", "USB-C to Lightning Cable 1m")]
    public async Task Page_Renders(string url, string expectedText)
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(url);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(expectedText, html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/Products/Details/999999")]
    [InlineData("/Orders/Details/999999")]
    [InlineData("/no/such/page")]
    public async Task MissingThings_Return404(string url)
    {
        var response = await _factory.CreateClient().GetAsync(url);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Health_ReportsHealthy()
    {
        var response = await _factory.CreateClient().GetAsync("/health");
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Responses_CarryACorrelationId()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Correlation-ID", "support-ticket-42");

        var response = await client.GetAsync("/");

        Assert.Equal("support-ticket-42", response.Headers.GetValues("X-Correlation-ID").Single());
    }

    [Fact]
    public async Task LowStockCsv_DownloadsAndEscapesFormulas()
    {
        var response = await _factory.CreateClient().GetAsync("/Reports/LowStockCsv");

        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("\"MOB-002\"", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal("\"'=HYPERLINK()\"", Csv.Escape("=HYPERLINK()"));
    }

    [Fact]
    public async Task CreateOrder_ThenAllocate_EndToEnd()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var form = await client.GetStringAsync("/Orders/Create");
        var created = await client.PostAsync("/Orders/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = Token(form),
            ["CustomerId"] = "6",
            ["WarehouseId"] = "1",
            ["Lines[0].ProductId"] = "15",
            ["Lines[0].Quantity"] = "2",
            ["Lines[1].ProductId"] = "0",
            ["Lines[1].Quantity"] = "",
        }));

        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var detailsUrl = created.Headers.Location!.OriginalString;
        Assert.Matches(@"^/Orders/Details/\d+$", detailsUrl);

        var details = await client.GetStringAsync(detailsUrl);
        var allocated = await client.PostAsync(detailsUrl.Replace("Details", "Allocate", StringComparison.Ordinal), new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = Token(details),
        }));
        Assert.Equal(HttpStatusCode.Redirect, allocated.StatusCode);

        var after = await client.GetStringAsync(detailsUrl);
        Assert.Contains("Stock allocated.", after, StringComparison.Ordinal);
        Assert.Contains(">Allocated<", after, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateOrder_WithNoLines_ShowsValidationErrors()
    {
        var client = _factory.CreateClient();
        var form = await client.GetStringAsync("/Orders/Create");

        var response = await client.PostAsync("/Orders/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = Token(form),
            ["CustomerId"] = "1",
            ["WarehouseId"] = "1",
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Add at least one product.", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    private static string Token(string html) => TokenPattern().Match(html).Groups[1].Value;

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenPattern();
}
