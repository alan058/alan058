using StockFlow.Core.Abstractions;
using StockFlow.Data.InMemory;

namespace StockFlow.Tests.Contracts;

public sealed class InMemoryContractTests : RepositoryContractTests
{
    private readonly InMemoryStockFlowStore _store = InMemoryStockFlowStore.CreateWithDemoData();

    protected override StockFlowRepositories Repositories => new(_store, _store, _store, _store);
}
