namespace StockFlow.Core.Models;

public sealed class SalesOrderSummary
{
    public int SalesOrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public int WarehouseId { get; set; }
    public string WarehouseCode { get; set; } = string.Empty;
    public OrderStatus Status { get; set; }
    public DateTime OrderDate { get; set; }
    public DateTime? RequiredDate { get; set; }
    public int LineCount { get; set; }
    public int TotalQuantity { get; set; }
    public decimal TotalValue { get; set; }
}

public sealed class SalesOrder
{
    public int SalesOrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public int WarehouseId { get; set; }
    public string WarehouseCode { get; set; } = string.Empty;
    public OrderStatus Status { get; set; }
    public DateTime OrderDate { get; set; }
    public DateTime? RequiredDate { get; set; }
    public string? Notes { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime? DispatchedAt { get; set; }
    public List<SalesOrderLine> Lines { get; set; } = new();

    public decimal TotalValue => Lines.Sum(l => l.LineTotal);
    public int TotalQuantity => Lines.Sum(l => l.Quantity);

    /// <summary>True when every line can be covered by stock available in the order's warehouse.</summary>
    public bool CanBeFullyAllocated => Lines.All(l => l.QuantityAvailable >= l.Quantity);
}

public sealed class SalesOrderLine
{
    public int SalesOrderLineId { get; set; }
    public int ProductId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }

    /// <summary>Stock available (on hand minus reserved) in the order's warehouse right now.</summary>
    public int QuantityAvailable { get; set; }

    public decimal LineTotal => Quantity * UnitPrice;
}

public sealed class NewSalesOrder
{
    public int CustomerId { get; set; }
    public int WarehouseId { get; set; }
    public DateTime? RequiredDate { get; set; }
    public string? Notes { get; set; }
    public List<NewOrderLine> Lines { get; set; } = new();
}

public sealed class NewOrderLine
{
    public NewOrderLine() { }

    public NewOrderLine(int productId, int quantity)
    {
        ProductId = productId;
        Quantity = quantity;
    }

    public int ProductId { get; set; }
    public int Quantity { get; set; }
}

public sealed class OrderQuery
{
    public OrderStatus? Status { get; set; }
    public int? CustomerId { get; set; }
    public string? SearchTerm { get; set; }
    public int Top { get; set; } = 200;
}
