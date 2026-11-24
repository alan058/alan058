namespace StockFlow.Core.Models;

/// <summary>Sales order lifecycle. Values match dbo.SalesOrder.Status.</summary>
public enum OrderStatus : byte
{
    Open = 0,
    Allocated = 1,
    Dispatched = 2,
    Cancelled = 3,
}

/// <summary>Kinds of stock movement. Values match dbo.StockMovement.MovementType.</summary>
public enum MovementType : byte
{
    Receipt = 1,
    Adjustment = 2,
    Allocation = 3,
    Release = 4,
    Dispatch = 5,
}
