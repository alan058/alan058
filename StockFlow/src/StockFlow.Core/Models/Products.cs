namespace StockFlow.Core.Models;

/// <summary>A product as stored, used for create and edit.</summary>
public sealed class Product
{
    public int ProductId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int ReorderLevel { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>SQL Server rowversion, used for optimistic concurrency on update.</summary>
    public byte[]? RowVersion { get; set; }

    public bool IsNew => ProductId == 0;
}

/// <summary>A product with its stock totalled across all warehouses (search results, grids).</summary>
public sealed class ProductSummary
{
    public int ProductId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int ReorderLevel { get; set; }
    public bool IsActive { get; set; }
    public int QuantityOnHand { get; set; }
    public int QuantityReserved { get; set; }

    public int QuantityAvailable => QuantityOnHand - QuantityReserved;
    public bool IsLowStock => QuantityAvailable <= ReorderLevel;
}

/// <summary>Stock for one product in one warehouse.</summary>
public sealed class StockLevel
{
    public int ProductId { get; set; }
    public int WarehouseId { get; set; }
    public string WarehouseCode { get; set; } = string.Empty;
    public string WarehouseName { get; set; } = string.Empty;
    public int QuantityOnHand { get; set; }
    public int QuantityReserved { get; set; }

    public int QuantityAvailable => QuantityOnHand - QuantityReserved;
}

public sealed class ProductDetail
{
    public ProductDetail(Product product, IReadOnlyList<StockLevel> stockLevels)
    {
        Product = product ?? throw new ArgumentNullException(nameof(product));
        StockLevels = stockLevels ?? throw new ArgumentNullException(nameof(stockLevels));
    }

    public Product Product { get; }
    public IReadOnlyList<StockLevel> StockLevels { get; }

    public int TotalOnHand => StockLevels.Sum(s => s.QuantityOnHand);
    public int TotalReserved => StockLevels.Sum(s => s.QuantityReserved);
    public int TotalAvailable => TotalOnHand - TotalReserved;
}

public sealed class ProductQuery
{
    public string? SearchTerm { get; set; }
    public int? CategoryId { get; set; }
    public bool LowStockOnly { get; set; }
    public bool IncludeInactive { get; set; }
}

public sealed class StockMovement
{
    public long StockMovementId { get; set; }
    public int ProductId { get; set; }
    public int WarehouseId { get; set; }
    public string WarehouseCode { get; set; } = string.Empty;
    public MovementType MovementType { get; set; }
    public int QuantityChange { get; set; }
    public int ReservedChange { get; set; }
    public string? Reference { get; set; }
    public string? Reason { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

/// <summary>A request to receive or adjust stock.</summary>
public sealed class StockAdjustment
{
    public int ProductId { get; set; }
    public int WarehouseId { get; set; }
    public MovementType MovementType { get; set; } = MovementType.Adjustment;
    public int QuantityChange { get; set; }
    public string? Reference { get; set; }
    public string? Reason { get; set; }
}
