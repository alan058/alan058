using StockFlow.Core.Models;

namespace StockFlow.Core.Services;

/// <summary>
/// The order state machine in one place. The UIs use it to enable buttons and
/// the services use it to reject invalid actions early; the stored procedures
/// enforce the same rules again inside the transaction.
/// </summary>
public static class OrderStatusRules
{
    public static bool CanAllocate(OrderStatus status) => status == OrderStatus.Open;

    public static bool CanDispatch(OrderStatus status) => status == OrderStatus.Allocated;

    public static bool CanCancel(OrderStatus status) => status is OrderStatus.Open or OrderStatus.Allocated;

    public static string Describe(OrderStatus status) => status switch
    {
        OrderStatus.Open => "Open",
        OrderStatus.Allocated => "Allocated",
        OrderStatus.Dispatched => "Dispatched",
        OrderStatus.Cancelled => "Cancelled",
        _ => status.ToString(),
    };
}
