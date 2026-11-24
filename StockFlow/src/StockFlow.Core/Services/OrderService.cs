using StockFlow.Core.Abstractions;
using StockFlow.Core.Exceptions;
using StockFlow.Core.Models;
using StockFlow.Core.Validation;

namespace StockFlow.Core.Services;

public sealed class OrderService
{
    public const int MaxLinesPerOrder = 200;

    private readonly IOrderRepository _orders;

    public OrderService(IOrderRepository orders)
    {
        _orders = orders ?? throw new ArgumentNullException(nameof(orders));
    }

    public Task<IReadOnlyList<SalesOrderSummary>> SearchAsync(OrderQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return _orders.SearchAsync(query, cancellationToken);
    }

    public async Task<SalesOrder> GetAsync(int salesOrderId, CancellationToken cancellationToken = default)
    {
        return await _orders.GetByIdAsync(salesOrderId, cancellationToken).ConfigureAwait(false)
            ?? throw EntityNotFoundException.For("Sales order", salesOrderId);
    }

    /// <summary>Validates and creates an Open order. Returns the new order id.</summary>
    public Task<int> CreateAsync(NewSalesOrder order, string user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);

        order.Lines = ConsolidateLines(order.Lines);
        order.Notes = string.IsNullOrWhiteSpace(order.Notes) ? null : order.Notes.Trim();

        var errors = new ValidationErrors()
            .AddIf(order.CustomerId <= 0, nameof(order.CustomerId), "Choose a customer.")
            .AddIf(order.WarehouseId <= 0, nameof(order.WarehouseId), "Choose a warehouse.")
            .AddIf(order.Lines.Count == 0, nameof(order.Lines), "Add at least one product.")
            .AddIf(order.Lines.Count > MaxLinesPerOrder, nameof(order.Lines), $"An order can have at most {MaxLinesPerOrder} lines.")
            .AddIf(order.Lines.Any(l => l.ProductId <= 0), nameof(order.Lines), "Every line needs a product.")
            .AddIf(order.Lines.Any(l => l.Quantity <= 0), nameof(order.Lines), "Every line quantity must be greater than zero.")
            .AddIf(order.Notes?.Length > 500, nameof(order.Notes), "Notes must be 500 characters or fewer.")
            .AddIf(order.RequiredDate.HasValue && order.RequiredDate.Value.Date < DateTime.UtcNow.Date,
                   nameof(order.RequiredDate), "Required date cannot be in the past.")
            .AddIf(string.IsNullOrWhiteSpace(user), "User", "The current user is unknown.");
        errors.ThrowIfAny();

        return _orders.CreateAsync(order, user, cancellationToken);
    }

    public async Task AllocateAsync(int salesOrderId, string user, CancellationToken cancellationToken = default)
    {
        await EnsureStatusAsync(salesOrderId, OrderStatusRules.CanAllocate, "allocated", cancellationToken).ConfigureAwait(false);
        await _orders.AllocateAsync(salesOrderId, user, cancellationToken).ConfigureAwait(false);
    }

    public async Task DispatchAsync(int salesOrderId, string user, CancellationToken cancellationToken = default)
    {
        await EnsureStatusAsync(salesOrderId, OrderStatusRules.CanDispatch, "dispatched", cancellationToken).ConfigureAwait(false);
        await _orders.DispatchAsync(salesOrderId, user, cancellationToken).ConfigureAwait(false);
    }

    public async Task CancelAsync(int salesOrderId, string user, CancellationToken cancellationToken = default)
    {
        await EnsureStatusAsync(salesOrderId, OrderStatusRules.CanCancel, "cancelled", cancellationToken).ConfigureAwait(false);
        await _orders.CancelAsync(salesOrderId, user, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Merges lines for the same product into one line.
    /// Root-cause fix for defect SF-112: pasting the same SKU twice into an
    /// order used to fail with a unique-key violation on SalesOrderLine.
    /// </summary>
    public static List<NewOrderLine> ConsolidateLines(IEnumerable<NewOrderLine>? lines)
    {
        if (lines is null)
        {
            return new List<NewOrderLine>();
        }

        return lines
            .Where(l => l is not null)
            .GroupBy(l => l.ProductId)
            .Select(g => new NewOrderLine(g.Key, g.Sum(l => l.Quantity)))
            .ToList();
    }

    /// <summary>
    /// Fails fast with a clear message before calling the database. The stored
    /// procedure re-checks inside its transaction, so a race still cannot slip through.
    /// </summary>
    private async Task EnsureStatusAsync(int salesOrderId, Func<OrderStatus, bool> isAllowed, string action, CancellationToken cancellationToken)
    {
        var order = await GetAsync(salesOrderId, cancellationToken).ConfigureAwait(false);
        if (!isAllowed(order.Status))
        {
            throw new InvalidOrderStateException(
                $"Order {order.OrderNumber} is {OrderStatusRules.Describe(order.Status).ToLowerInvariant()} and cannot be {action}.");
        }
    }
}
