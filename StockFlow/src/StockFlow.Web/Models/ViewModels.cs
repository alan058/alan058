using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using StockFlow.Core.Models;

namespace StockFlow.Web.Models;

public sealed class DashboardViewModel
{
    public DashboardSummary Summary { get; set; } = new();
    public IReadOnlyList<LowStockItem> LowStock { get; set; } = Array.Empty<LowStockItem>();
    public IReadOnlyList<SalesOrderSummary> RecentOrders { get; set; } = Array.Empty<SalesOrderSummary>();
}

public sealed class ProductIndexViewModel
{
    public string? Q { get; set; }
    public int? CategoryId { get; set; }
    public bool LowStock { get; set; }
    public bool IncludeInactive { get; set; }
    public IReadOnlyList<ProductSummary> Products { get; set; } = Array.Empty<ProductSummary>();
    public IEnumerable<SelectListItem> Categories { get; set; } = Array.Empty<SelectListItem>();
}

public sealed class ProductDetailsViewModel
{
    public ProductDetailsViewModel(ProductDetail detail, IReadOnlyList<StockMovement> movements)
    {
        Detail = detail;
        Movements = movements;
    }

    public ProductDetail Detail { get; }
    public IReadOnlyList<StockMovement> Movements { get; }
    public Product Product => Detail.Product;
}

public sealed class ProductEditViewModel
{
    public int ProductId { get; set; }

    [Required, StringLength(32, MinimumLength = 3), Display(Name = "SKU")]
    public string Sku { get; set; } = string.Empty;

    [Required, StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [Range(1, int.MaxValue, ErrorMessage = "Choose a category."), Display(Name = "Category")]
    public int CategoryId { get; set; }

    [Range(0, 1_000_000), Display(Name = "Unit price")]
    public decimal UnitPrice { get; set; }

    [Range(0, int.MaxValue), Display(Name = "Reorder level")]
    public int ReorderLevel { get; set; }

    [Display(Name = "Active")]
    public bool IsActive { get; set; } = true;

    /// <summary>Base64 rowversion round-tripped through the form for optimistic concurrency.</summary>
    public string? RowVersion { get; set; }

    public IEnumerable<SelectListItem> Categories { get; set; } = Array.Empty<SelectListItem>();

    public bool IsNew => ProductId == 0;

    public static ProductEditViewModel From(Product p) => new()
    {
        ProductId = p.ProductId,
        Sku = p.Sku,
        Name = p.Name,
        CategoryId = p.CategoryId,
        UnitPrice = p.UnitPrice,
        ReorderLevel = p.ReorderLevel,
        IsActive = p.IsActive,
        RowVersion = p.RowVersion is null ? null : Convert.ToBase64String(p.RowVersion),
    };

    public Product ToProduct() => new()
    {
        ProductId = ProductId,
        Sku = Sku,
        Name = Name,
        CategoryId = CategoryId,
        UnitPrice = UnitPrice,
        ReorderLevel = ReorderLevel,
        IsActive = IsActive,
        RowVersion = string.IsNullOrEmpty(RowVersion) ? null : Convert.FromBase64String(RowVersion),
    };
}

public enum StockActionMode
{
    Receive,
    Adjust,
}

public sealed class StockActionViewModel
{
    public int ProductId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public StockActionMode Mode { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Choose a warehouse."), Display(Name = "Warehouse")]
    public int WarehouseId { get; set; }

    [Display(Name = "Quantity")]
    public int Quantity { get; set; }

    [StringLength(50), Display(Name = "Delivery / PO reference")]
    public string? Reference { get; set; }

    [StringLength(200)]
    public string? Reason { get; set; }

    public IReadOnlyList<StockLevel> CurrentStock { get; set; } = Array.Empty<StockLevel>();
    public IEnumerable<SelectListItem> Warehouses { get; set; } = Array.Empty<SelectListItem>();
}

public sealed class OrderIndexViewModel
{
    public OrderStatus? Status { get; set; }
    public string? Q { get; set; }
    public IReadOnlyList<SalesOrderSummary> Orders { get; set; } = Array.Empty<SalesOrderSummary>();
}

public sealed class OrderCreateViewModel
{
    public const int BlankLines = 6;

    [Range(1, int.MaxValue, ErrorMessage = "Choose a customer."), Display(Name = "Customer")]
    public int CustomerId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Choose a warehouse."), Display(Name = "Ship from")]
    public int WarehouseId { get; set; }

    [DataType(DataType.Date), Display(Name = "Required by")]
    public DateTime? RequiredDate { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }

    public List<OrderLineInput> Lines { get; set; } = new();

    public IEnumerable<SelectListItem> Customers { get; set; } = Array.Empty<SelectListItem>();
    public IEnumerable<SelectListItem> Warehouses { get; set; } = Array.Empty<SelectListItem>();
    public IEnumerable<SelectListItem> Products { get; set; } = Array.Empty<SelectListItem>();

    /// <summary>Pads the line list with empty rows so the form always has room to add products.</summary>
    public void EnsureBlankLines()
    {
        while (Lines.Count < BlankLines)
        {
            Lines.Add(new OrderLineInput());
        }
    }

    public NewSalesOrder ToNewOrder() => new()
    {
        CustomerId = CustomerId,
        WarehouseId = WarehouseId,
        RequiredDate = RequiredDate,
        Notes = Notes,
        // Rows left blank on the form are ignored rather than treated as errors.
        Lines = Lines.Where(l => l.ProductId > 0 || (l.Quantity ?? 0) != 0)
                     .Select(l => new NewOrderLine(l.ProductId, l.Quantity ?? 0))
                     .ToList(),
    };
}

public sealed class OrderLineInput
{
    public int ProductId { get; set; }

    /// <summary>Nullable because blank rows post an empty string, which must not fail model binding.</summary>
    public int? Quantity { get; set; }
}

public sealed class ErrorViewModel
{
    public string? RequestId { get; set; }
}
