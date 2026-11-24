using StockFlow.Core.Exceptions;
using StockFlow.Core.Services;
using StockFlow.Data.InMemory;

namespace StockFlow.Tests.Services;

public sealed class InventoryServiceTests
{
    private readonly InMemoryStockFlowStore _store = InMemoryStockFlowStore.CreateWithDemoData();
    private readonly InventoryService _service;

    public InventoryServiceTests() => _service = new InventoryService(_store);

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(InventoryService.MaxQuantityPerMovement + 1)]
    public async Task Receive_RejectsQuantitiesOutOfRange(int quantity)
    {
        await Assert.ThrowsAsync<BusinessValidationException>(() => _service.ReceiveAsync(1, 1, quantity, null, "tester"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Adjust_RequiresAReason(string? reason)
    {
        var ex = await Assert.ThrowsAsync<BusinessValidationException>(() => _service.AdjustAsync(1, 1, -1, reason, "tester"));
        Assert.True(ex.Errors.ContainsKey("Reason"));
    }

    [Fact]
    public async Task Adjust_CannotTakeStockThatIsReservedForAnOrder()
    {
        // Demo order SO-000001 reserves 10 x AUD-001 (product 1) in JHB, which holds 120.
        var ex = await Assert.ThrowsAsync<InsufficientStockException>(() => _service.AdjustAsync(1, 1, -111, "Count variance", "tester"));
        Assert.Contains("110", ex.Message, StringComparison.Ordinal);

        var level = await _service.AdjustAsync(1, 1, -110, "Count variance", "tester");
        Assert.Equal(10, level.QuantityOnHand);
        Assert.Equal(10, level.QuantityReserved);
        Assert.Equal(0, level.QuantityAvailable);
    }

    [Fact]
    public async Task Receive_AddsToOnHandAndRecordsTheReference()
    {
        var level = await _service.ReceiveAsync(6, 3, 12, "  PO-1001 ", "tester");

        Assert.Equal(12, level.QuantityOnHand);
        var movement = (await _store.GetMovementsAsync(6, 1)).Single();
        Assert.Equal("PO-1001", movement.Reference);
        Assert.Equal("tester", movement.CreatedBy);
    }
}
