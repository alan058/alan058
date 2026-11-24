namespace StockFlow.Core.Models;

public sealed class DashboardSummary
{
    public int ActiveProducts { get; set; }
    public int LowStockProducts { get; set; }
    public decimal StockValue { get; set; }
    public int OpenOrders { get; set; }
    public int AllocatedOrders { get; set; }
    public int DispatchedToday { get; set; }
}

public sealed class LowStockItem
{
    public int ProductId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public int ReorderLevel { get; set; }
    public int QuantityOnHand { get; set; }
    public int QuantityReserved { get; set; }
    public int QuantityAvailable { get; set; }
    public int SuggestedOrderQuantity { get; set; }
}

/// <summary>An item for a drop-down list.</summary>
public sealed class LookupItem
{
    public LookupItem() { }

    public LookupItem(int id, string? code, string name)
    {
        Id = id;
        Code = code;
        Name = name;
    }

    public int Id { get; set; }
    public string? Code { get; set; }
    public string Name { get; set; } = string.Empty;

    public string DisplayName => string.IsNullOrEmpty(Code) ? Name : $"{Code} - {Name}";

    public override string ToString() => DisplayName;
}

public sealed class Lookups
{
    public IReadOnlyList<LookupItem> Categories { get; set; } = Array.Empty<LookupItem>();
    public IReadOnlyList<LookupItem> Warehouses { get; set; } = Array.Empty<LookupItem>();
    public IReadOnlyList<LookupItem> Customers { get; set; } = Array.Empty<LookupItem>();
}
