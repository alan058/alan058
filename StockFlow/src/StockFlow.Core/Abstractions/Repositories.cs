using StockFlow.Core.Models;

namespace StockFlow.Core.Abstractions;

/*
 * Repository contracts. Two implementations exist in StockFlow.Data:
 *   - SqlServer: calls the stored procedures in /database.
 *   - InMemory:  same behaviour without a database (demos and fast tests).
 * The shared contract tests in StockFlow.Tests run against both, so they
 * cannot drift apart.
 *
 * Implementations throw StockFlowException subclasses for business failures
 * and DataAccessException for infrastructure failures.
 */

public interface IProductRepository
{
    Task<IReadOnlyList<ProductSummary>> SearchAsync(ProductQuery query, CancellationToken cancellationToken = default);

    Task<ProductDetail?> GetByIdAsync(int productId, CancellationToken cancellationToken = default);

    /// <summary>Inserts or updates the product and returns its id.</summary>
    Task<int> SaveAsync(Product product, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StockMovement>> GetMovementsAsync(int productId, int top, CancellationToken cancellationToken = default);
}

public interface IInventoryRepository
{
    /// <summary>Posts a receipt or adjustment and returns the resulting stock level.</summary>
    Task<StockLevel> AdjustAsync(StockAdjustment adjustment, string user, CancellationToken cancellationToken = default);
}

public interface IOrderRepository
{
    Task<IReadOnlyList<SalesOrderSummary>> SearchAsync(OrderQuery query, CancellationToken cancellationToken = default);

    Task<SalesOrder?> GetByIdAsync(int salesOrderId, CancellationToken cancellationToken = default);

    Task<int> CreateAsync(NewSalesOrder order, string user, CancellationToken cancellationToken = default);

    Task AllocateAsync(int salesOrderId, string user, CancellationToken cancellationToken = default);

    Task DispatchAsync(int salesOrderId, string user, CancellationToken cancellationToken = default);

    Task CancelAsync(int salesOrderId, string user, CancellationToken cancellationToken = default);
}

public interface IReportRepository
{
    Task<DashboardSummary> GetDashboardAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LowStockItem>> GetLowStockAsync(CancellationToken cancellationToken = default);

    Task<Lookups> GetLookupsAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns true when the data store can be reached.</summary>
    Task<bool> PingAsync(CancellationToken cancellationToken = default);
}

/// <summary>The full set of repositories, handy for non-DI hosts such as the WinForms app.</summary>
public sealed class StockFlowRepositories
{
    public StockFlowRepositories(
        IProductRepository products,
        IInventoryRepository inventory,
        IOrderRepository orders,
        IReportRepository reports)
    {
        Products = products ?? throw new ArgumentNullException(nameof(products));
        Inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        Orders = orders ?? throw new ArgumentNullException(nameof(orders));
        Reports = reports ?? throw new ArgumentNullException(nameof(reports));
    }

    public IProductRepository Products { get; }
    public IInventoryRepository Inventory { get; }
    public IOrderRepository Orders { get; }
    public IReportRepository Reports { get; }
}
