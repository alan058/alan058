using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using StockFlow.Core.Abstractions;
using StockFlow.Core.Exceptions;
using StockFlow.Core.Models;
using StockFlow.Core.Services;
using StockFlow.Web.Models;

namespace StockFlow.Web.Controllers;

public sealed class ProductsController : StockFlowController
{
    private readonly ProductService _products;
    private readonly InventoryService _inventory;
    private readonly IReportRepository _reports;

    public ProductsController(ProductService products, InventoryService inventory, IReportRepository reports)
    {
        _products = products;
        _inventory = inventory;
        _reports = reports;
    }

    public async Task<IActionResult> Index(string? q, int? categoryId, bool lowStock, bool includeInactive, CancellationToken cancellationToken)
    {
        var lookups = await _reports.GetLookupsAsync(cancellationToken);
        var products = await _products.SearchAsync(new ProductQuery
        {
            SearchTerm = q,
            CategoryId = categoryId,
            LowStockOnly = lowStock,
            IncludeInactive = includeInactive,
        }, cancellationToken);

        return View(new ProductIndexViewModel
        {
            Q = q,
            CategoryId = categoryId,
            LowStock = lowStock,
            IncludeInactive = includeInactive,
            Products = products,
            Categories = new SelectList(lookups.Categories, nameof(LookupItem.Id), nameof(LookupItem.Name), categoryId),
        });
    }

    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        var detail = await _products.GetAsync(id, cancellationToken);
        var movements = await _products.GetMovementsAsync(id, 25, cancellationToken);
        return View(new ProductDetailsViewModel(detail, movements));
    }

    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        var model = new ProductEditViewModel();
        await FillLookupsAsync(model, cancellationToken);
        return View("Edit", model);
    }

    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var detail = await _products.GetAsync(id, cancellationToken);
        var model = ProductEditViewModel.From(detail.Product);
        await FillLookupsAsync(model, cancellationToken);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(ProductEditViewModel model, CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            try
            {
                var id = await _products.SaveAsync(model.ToProduct(), cancellationToken);
                Success(model.IsNew ? $"Product {model.Sku.ToUpperInvariant()} created." : "Product saved.");
                return RedirectToAction(nameof(Details), new { id });
            }
            catch (StockFlowException ex)
            {
                AddErrors(ex);
            }
        }

        await FillLookupsAsync(model, cancellationToken);
        return View("Edit", model);
    }

    public Task<IActionResult> Receive(int id, CancellationToken cancellationToken) =>
        StockActionForm(id, StockActionMode.Receive, cancellationToken);

    public Task<IActionResult> Adjust(int id, CancellationToken cancellationToken) =>
        StockActionForm(id, StockActionMode.Adjust, cancellationToken);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PostStock(StockActionViewModel model, CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            try
            {
                var level = model.Mode == StockActionMode.Receive
                    ? await _inventory.ReceiveAsync(model.ProductId, model.WarehouseId, model.Quantity, model.Reference, CurrentUser, cancellationToken)
                    : await _inventory.AdjustAsync(model.ProductId, model.WarehouseId, model.Quantity, model.Reason, CurrentUser, cancellationToken);

                Success($"{(model.Mode == StockActionMode.Receive ? "Received" : "Adjusted")} {model.Quantity:+#;-#;0} at {level.WarehouseCode}. " +
                        $"On hand is now {level.QuantityOnHand}, available {level.QuantityAvailable}.");
                return RedirectToAction(nameof(Details), new { id = model.ProductId });
            }
            catch (StockFlowException ex)
            {
                AddErrors(ex);
            }
        }

        await FillStockActionAsync(model, cancellationToken);
        return View("StockAction", model);
    }

    private async Task<IActionResult> StockActionForm(int id, StockActionMode mode, CancellationToken cancellationToken)
    {
        var model = new StockActionViewModel { ProductId = id, Mode = mode };
        await FillStockActionAsync(model, cancellationToken);
        return View("StockAction", model);
    }

    private async Task FillStockActionAsync(StockActionViewModel model, CancellationToken cancellationToken)
    {
        var detail = await _products.GetAsync(model.ProductId, cancellationToken);
        model.Sku = detail.Product.Sku;
        model.ProductName = detail.Product.Name;
        model.CurrentStock = detail.StockLevels;
        model.Warehouses = detail.StockLevels.Select(s => new SelectListItem($"{s.WarehouseCode} - {s.WarehouseName}", s.WarehouseId.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    private async Task FillLookupsAsync(ProductEditViewModel model, CancellationToken cancellationToken)
    {
        var lookups = await _reports.GetLookupsAsync(cancellationToken);
        model.Categories = new SelectList(lookups.Categories, nameof(LookupItem.Id), nameof(LookupItem.Name), model.CategoryId);
    }
}
