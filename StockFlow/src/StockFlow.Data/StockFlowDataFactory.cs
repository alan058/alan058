using StockFlow.Core.Abstractions;
using StockFlow.Data.InMemory;
using StockFlow.Data.SqlServer;

namespace StockFlow.Data;

/// <summary>Builds the repositories for the configured provider. Used by every host (web, desktop, tests).</summary>
public static class StockFlowDataFactory
{
    public static StockFlowRepositories Create(DataOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        switch (options.Provider)
        {
            case DataProvider.SqlServer:
                var executor = new SqlExecutor(options);
                return new StockFlowRepositories(
                    new SqlProductRepository(executor),
                    new SqlInventoryRepository(executor),
                    new SqlOrderRepository(executor),
                    new SqlReportRepository(executor));

            case DataProvider.InMemory:
                var store = InMemoryStockFlowStore.CreateWithDemoData();
                return new StockFlowRepositories(store, store, store, store);

            default:
                throw new NotSupportedException($"Data provider '{options.Provider}' is not supported.");
        }
    }
}
