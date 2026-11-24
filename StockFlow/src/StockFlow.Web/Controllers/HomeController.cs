using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using StockFlow.Core.Abstractions;
using StockFlow.Core.Models;
using StockFlow.Web.Models;

namespace StockFlow.Web.Controllers;

public sealed class HomeController : StockFlowController
{
    private readonly IReportRepository _reports;
    private readonly IOrderRepository _orders;
    private readonly ILogger<HomeController> _logger;

    public HomeController(IReportRepository reports, IOrderRepository orders, ILogger<HomeController> logger)
    {
        _reports = reports;
        _orders = orders;
        _logger = logger;
    }

    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        // Independent queries run in parallel to keep the dashboard fast.
        var summary = _reports.GetDashboardAsync(cancellationToken);
        var lowStock = _reports.GetLowStockAsync(cancellationToken);
        var recent = _orders.SearchAsync(new OrderQuery { Top = 8 }, cancellationToken);
        await Task.WhenAll(summary, lowStock, recent);

        return View(new DashboardViewModel
        {
            Summary = await summary,
            LowStock = (await lowStock).Take(5).ToList(),
            RecentOrders = await recent,
        });
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        var feature = HttpContext.Features.Get<IExceptionHandlerPathFeature>();
        if (feature?.Error is not null)
        {
            _logger.LogError(feature.Error, "Unhandled exception on {Path} (correlation {CorrelationId})", feature.Path, HttpContext.TraceIdentifier);
        }

        return View(new ErrorViewModel { RequestId = HttpContext.TraceIdentifier });
    }

    [Route("Home/StatusCode")]
    public IActionResult StatusCodePage(int code)
    {
        Response.StatusCode = code;
        return code == StatusCodes.Status404NotFound
            ? View("NotFound", "The page you asked for does not exist.")
            : View("Error", new ErrorViewModel { RequestId = HttpContext.TraceIdentifier });
    }
}
