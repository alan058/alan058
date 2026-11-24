using StockFlow.Core.Abstractions;
using StockFlow.Core.Exceptions;
using StockFlow.Core.Models;
using StockFlow.Core.Services;
using StockFlow.Data.InMemory;

namespace StockFlow.Tests.Services;

public sealed class OrderServiceTests
{
    private readonly InMemoryStockFlowStore _store = InMemoryStockFlowStore.CreateWithDemoData();
    private readonly OrderService _service;

    public OrderServiceTests() => _service = new OrderService(_store);

    [Fact]
    public void ConsolidateLines_MergesTheSameProduct()
    {
        // Regression test for SF-112 (duplicate SKU on an order caused a unique-key error).
        var lines = OrderService.ConsolidateLines(new[]
        {
            new NewOrderLine(1, 2),
            new NewOrderLine(2, 1),
            new NewOrderLine(1, 3),
        });

        Assert.Equal(2, lines.Count);
        Assert.Equal(5, lines.Single(l => l.ProductId == 1).Quantity);
    }

    [Fact]
    public async Task Create_WithDuplicateProductLines_Succeeds()
    {
        var id = await _service.CreateAsync(new NewSalesOrder
        {
            CustomerId = 1,
            WarehouseId = 1,
            Lines = { new NewOrderLine(3, 1), new NewOrderLine(3, 1) },
        }, "tester");

        var order = await _service.GetAsync(id);
        Assert.Equal(2, order.Lines.Single().Quantity);
    }

    [Fact]
    public async Task Create_ReportsAllHeaderProblems()
    {
        var ex = await Assert.ThrowsAsync<BusinessValidationException>(() =>
            _service.CreateAsync(new NewSalesOrder { RequiredDate = DateTime.UtcNow.AddDays(-2) }, "tester"));

        Assert.Contains(nameof(NewSalesOrder.CustomerId), ex.Errors.Keys);
        Assert.Contains(nameof(NewSalesOrder.WarehouseId), ex.Errors.Keys);
        Assert.Contains(nameof(NewSalesOrder.Lines), ex.Errors.Keys);
        Assert.Contains(nameof(NewSalesOrder.RequiredDate), ex.Errors.Keys);
    }

    [Fact]
    public async Task Dispatch_OpenOrder_IsRejectedWithAClearMessage()
    {
        // Demo order 2 is Open.
        var ex = await Assert.ThrowsAsync<InvalidOrderStateException>(() => _service.DispatchAsync(2, "tester"));
        Assert.Equal("Order SO-000002 is open and cannot be dispatched.", ex.Message);
    }

    [Fact]
    public async Task Allocate_WhenStockIsShort_ExplainsWhichLines()
    {
        // Demo order 5 asks for 30 x AUD-002 but JHB only holds 18.
        var ex = await Assert.ThrowsAsync<InsufficientStockException>(() => _service.AllocateAsync(5, "tester"));
        Assert.Contains("AUD-002 (requested 30, available 18)", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(OrderStatus.Open, true, false, true)]
    [InlineData(OrderStatus.Allocated, false, true, true)]
    [InlineData(OrderStatus.Dispatched, false, false, false)]
    [InlineData(OrderStatus.Cancelled, false, false, false)]
    public void StatusRules_MatchTheOrderLifecycle(OrderStatus status, bool allocate, bool dispatch, bool cancel)
    {
        Assert.Equal(allocate, OrderStatusRules.CanAllocate(status));
        Assert.Equal(dispatch, OrderStatusRules.CanDispatch(status));
        Assert.Equal(cancel, OrderStatusRules.CanCancel(status));
    }

    [Fact]
    public async Task ReturnedObjects_AreCopies_SoCallersCannotCorruptTheStore()
    {
        IProductRepository products = _store;
        var detail = await products.GetByIdAsync(1);
        detail!.Product.Name = "changed by caller";

        var again = await products.GetByIdAsync(1);
        Assert.Equal("Wireless Earbuds Pro", again!.Product.Name);
    }
}
