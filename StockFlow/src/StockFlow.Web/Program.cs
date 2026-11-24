using StockFlow.Core.Abstractions;
using StockFlow.Core.Services;
using StockFlow.Data;
using StockFlow.Web.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// ---- Data access: provider and connection string come from configuration
//      (appsettings.json, environment variables, or user secrets).
var dataOptions = builder.Configuration.GetSection(DataOptions.SectionName).Get<DataOptions>() ?? new DataOptions();
var repositories = StockFlowDataFactory.Create(dataOptions);

builder.Services.AddSingleton(dataOptions);
builder.Services.AddSingleton(repositories.Products);
builder.Services.AddSingleton(repositories.Inventory);
builder.Services.AddSingleton(repositories.Orders);
builder.Services.AddSingleton(repositories.Reports);

// ---- Business services (stateless, so singletons are safe).
builder.Services.AddSingleton<ProductService>();
builder.Services.AddSingleton<InventoryService>();
builder.Services.AddSingleton<OrderService>();

builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add<StockFlowExceptionFilter>();
});

builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");

var app = builder.Build();

app.Logger.LogInformation("StockFlow web starting with data provider {Provider}", dataOptions.Provider);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseStatusCodePagesWithReExecute("/Home/StatusCode", "?code={0}");
app.UseStaticFiles();
app.UseRouting();

app.MapHealthChecks("/health");
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

/// <summary>Exposed so integration tests can host the app with WebApplicationFactory.</summary>
public partial class Program
{
}
