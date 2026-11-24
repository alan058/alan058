using StockFlow.Core.Abstractions;
using StockFlow.Core.Models;

namespace StockFlow.Data.SqlServer;

public sealed class SqlReportRepository : IReportRepository
{
    private readonly SqlExecutor _sql;

    public SqlReportRepository(SqlExecutor sql) => _sql = sql ?? throw new ArgumentNullException(nameof(sql));

    public Task<DashboardSummary> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
        return _sql.QueryAsync(
            "dbo.usp_Dashboard_Summary",
            null,
            async (r, ct) =>
            {
                await r.ReadAsync(ct).ConfigureAwait(false);
                return new DashboardSummary
                {
                    ActiveProducts = r.GetInt32("ActiveProducts"),
                    LowStockProducts = r.GetInt32("LowStockProducts"),
                    StockValue = r.GetDecimal("StockValue"),
                    OpenOrders = r.GetInt32("OpenOrders"),
                    AllocatedOrders = r.GetInt32("AllocatedOrders"),
                    DispatchedToday = r.GetInt32("DispatchedToday"),
                };
            },
            cancellationToken);
    }

    public Task<IReadOnlyList<LowStockItem>> GetLowStockAsync(CancellationToken cancellationToken = default)
    {
        return _sql.QueryAsync<IReadOnlyList<LowStockItem>>(
            "dbo.usp_Report_LowStock",
            null,
            async (r, ct) => await r.ReadAllAsync(row => new LowStockItem
            {
                ProductId = row.GetInt32("ProductId"),
                Sku = row.GetString("Sku"),
                Name = row.GetString("Name"),
                CategoryName = row.GetString("CategoryName"),
                ReorderLevel = row.GetInt32("ReorderLevel"),
                QuantityOnHand = row.GetInt32("QuantityOnHand"),
                QuantityReserved = row.GetInt32("QuantityReserved"),
                QuantityAvailable = row.GetInt32("QuantityAvailable"),
                SuggestedOrderQuantity = row.GetInt32("SuggestedOrderQuantity"),
            }, ct).ConfigureAwait(false),
            cancellationToken);
    }

    public Task<Lookups> GetLookupsAsync(CancellationToken cancellationToken = default)
    {
        return _sql.QueryAsync(
            "dbo.usp_Lookup_GetAll",
            null,
            async (r, ct) =>
            {
                static LookupItem Map(Microsoft.Data.SqlClient.SqlDataReader row) =>
                    new(row.GetInt32("Id"), row.GetNullableString("Code"), row.GetString("Name"));

                var categories = await r.ReadAllAsync(Map, ct).ConfigureAwait(false);
                await r.NextResultAsync(ct).ConfigureAwait(false);
                var warehouses = await r.ReadAllAsync(Map, ct).ConfigureAwait(false);
                await r.NextResultAsync(ct).ConfigureAwait(false);
                var customers = await r.ReadAllAsync(Map, ct).ConfigureAwait(false);

                return new Lookups { Categories = categories, Warehouses = warehouses, Customers = customers };
            },
            cancellationToken);
    }

    public async Task<bool> PingAsync(CancellationToken cancellationToken = default)
    {
        return await _sql.QueryAsync(
            "dbo.usp_Health_Ping",
            null,
            async (r, ct) => await r.ReadAsync(ct).ConfigureAwait(false) && r.GetBoolean(0),
            cancellationToken).ConfigureAwait(false);
    }
}
