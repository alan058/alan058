using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using StockFlow.Core.Abstractions;
using StockFlow.Core.Exceptions;
using StockFlow.Core.Models;
using StockFlow.Core.Services;
using StockFlow.Web.Models;

namespace StockFlow.Web.Controllers;

public sealed class OrdersController : StockFlowController
{
    private readonly OrderService _orders;
    private readonly ProductService _products;
    private readonly IReportRepository _reports;
    private readonly ILogger<OrdersController> _logger;

    public OrdersController(OrderService orders, ProductService products, IReportRepository reports, ILogger<OrdersController> logger)
    {
        _orders = orders;
        _products = products;
        _reports = reports;
        _logger = logger;
    }

    public async Task<IActionResult> Index(OrderStatus? status, string? q, CancellationToken cancellationToken)
    {
        var orders = await _orders.SearchAsync(new OrderQuery { Status = status, SearchTerm = q }, cancellationToken);
        return View(new OrderIndexViewModel { Status = status, Q = q, Orders = orders });
    }

    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        return View(await _orders.GetAsync(id, cancellationToken));
    }

    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        var model = new OrderCreateViewModel();
        await FillCreateAsync(model, cancellationToken);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(OrderCreateViewModel model, CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            try
            {
                var id = await _orders.CreateAsync(model.ToNewOrder(), CurrentUser, cancellationToken);
                Success("Order created. Allocate it to reserve the stock.");
                return RedirectToAction(nameof(Details), new { id });
            }
            catch (StockFlowException ex)
            {
                AddErrors(ex);
            }
        }

        await FillCreateAsync(model, cancellationToken);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Allocate(int id, CancellationToken cancellationToken) =>
        RunAsync(id, () => _orders.AllocateAsync(id, CurrentUser, cancellationToken), "Stock allocated.");

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Dispatch(int id, CancellationToken cancellationToken) =>
        RunAsync(id, () => _orders.DispatchAsync(id, CurrentUser, cancellationToken), "Order dispatched.");

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Cancel(int id, CancellationToken cancellationToken) =>
        RunAsync(id, () => _orders.CancelAsync(id, CurrentUser, cancellationToken), "Order cancelled. Any reserved stock was released.");

    /// <summary>Runs a status change and reports the outcome on the order page (Post/Redirect/Get).</summary>
    private async Task<IActionResult> RunAsync(int id, Func<Task> action, string successMessage)
    {
        try
        {
            await action();
            Success(successMessage);
        }
        catch (EntityNotFoundException)
        {
            throw; // 404 via the exception filter
        }
        catch (StockFlowException ex)
        {
            _logger.LogInformation("Order {OrderId} action rejected: {Reason}", id, ex.Message);
            Error(ex.Message);
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task FillCreateAsync(OrderCreateViewModel model, CancellationToken cancellationToken)
    {
        var lookups = await _reports.GetLookupsAsync(cancellationToken);
        var products = await _products.SearchAsync(new ProductQuery(), cancellationToken);

        model.Customers = new SelectList(lookups.Customers, nameof(LookupItem.Id), nameof(LookupItem.DisplayName), model.CustomerId);
        model.Warehouses = new SelectList(lookups.Warehouses, nameof(LookupItem.Id), nameof(LookupItem.DisplayName), model.WarehouseId);
        model.Products = products.Select(p => new SelectListItem(
            $"{p.Sku} - {p.Name} ({p.QuantityAvailable} available)",
            p.ProductId.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        model.EnsureBlankLines();
    }
}
