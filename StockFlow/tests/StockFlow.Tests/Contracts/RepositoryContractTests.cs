using StockFlow.Core.Abstractions;
using StockFlow.Core.Exceptions;
using StockFlow.Core.Models;

namespace StockFlow.Tests.Contracts;

/// <summary>
/// Behaviour every repository implementation must have. The same tests run
/// against the in-memory store and against a real SQL Server database, which
/// keeps the demo provider honest and gives the stored procedures a
/// regression suite.
///
/// Each test creates its own products and orders, so tests do not depend on
/// each other or on the order they run in.
/// </summary>
public abstract class RepositoryContractTests
{
    protected const string User = "contract-test";
    private const int Jhb = 1;
    private const int Cpt = 2;
    private const int Customer = 1;

    protected abstract StockFlowRepositories Repositories { get; }

    /// <summary>When false (e.g. no SQL Server configured) the tests are reported as skipped.</summary>
    protected virtual bool IsAvailable => true;

    private IProductRepository Products => Repositories.Products;
    private IInventoryRepository Inventory => Repositories.Inventory;
    private IOrderRepository Orders => Repositories.Orders;
    private IReportRepository Reports => Repositories.Reports;

    [SkippableFact]
    public async Task Lookups_ContainTheReferenceData()
    {
        Skip.IfNot(IsAvailable, "Data store not available");

        var lookups = await Reports.GetLookupsAsync();

        Assert.Equal(5, lookups.Categories.Count);
        Assert.Equal(new[] { "CPT", "DBN", "JHB" }, lookups.Warehouses.Select(w => w.Code));
        Assert.Contains(lookups.Customers, c => c.Id == Customer);
    }

    [SkippableFact]
    public async Task Product_SaveThenGet_RoundTripsAndListsEveryWarehouse()
    {
        Skip.IfNot(IsAvailable, "Data store not available");

        var sku = NewSku();
        var id = await Products.SaveAsync(new Product { Sku = sku, Name = "Round trip", CategoryId = 2, UnitPrice = 123.45m, ReorderLevel = 7 });

        var detail = await Products.GetByIdAsync(id);

        Assert.NotNull(detail);
        Assert.Equal(sku, detail!.Product.Sku);
        Assert.Equal("Computing", detail.Product.CategoryName);
        Assert.Equal(123.45m, detail.Product.UnitPrice);
        Assert.Equal(3, detail.StockLevels.Count);
        Assert.All(detail.StockLevels, s => Assert.Equal(0, s.QuantityOnHand));
        Assert.NotNull(detail.Product.RowVersion);
    }

    [SkippableFact]
    public async Task Product_DuplicateSku_IsRejected()
    {
        Skip.IfNot(IsAvailable, "Data store not available");

        var sku = NewSku();
        await Products.SaveAsync(new Product { Sku = sku, Name = "First", CategoryId = 1 });

        await Assert.ThrowsAsync<BusinessValidationException>(() =>
            Products.SaveAsync(new Product { Sku = sku, Name = "Second", CategoryId = 1 }));
    }

    [SkippableFact]
    public async Task Product_SaveWithStaleRowVersion_IsRejected()
    {
        Skip.IfNot(IsAvailable, "Data store not available");

        var id = await Products.SaveAsync(new Product { Sku = NewSku(), Name = "Original", CategoryId = 1 });
        var userA = (await Products.GetByIdAsync(id))!.Product;
        var userB = (await Products.GetByIdAsync(id))!.Product;

        userA.Name = "Saved by A";
        await Products.SaveAsync(userA);

        userB.Name = "Saved by B";
        await Assert.ThrowsAsync<ConcurrencyException>(() => Products.SaveAsync(userB));
        Assert.Equal("Saved by A", (await Products.GetByIdAsync(id))!.Product.Name);
    }

    [SkippableFact]
    public async Task ProductSearch_TreatsWildcardCharactersLiterally()
    {
        Skip.IfNot(IsAvailable, "Data store not available");

        var marker = NewSku();
        var id = await Products.SaveAsync(new Product { Sku = NewSku(), Name = $"{marker} 100% cotton_strap", CategoryId = 3 });
        await Products.SaveAsync(new Product { Sku = NewSku(), Name = $"{marker} 1000 cottonXstrap", CategoryId = 3 });

        var results = await Products.SearchAsync(new ProductQuery { SearchTerm = "0% cotton_" });

        Assert.Equal(id, Assert.Single(results).ProductId);
    }

    [SkippableFact]
    public async Task Receive_IncreasesStockAndWritesAMovement()
    {
        Skip.IfNot(IsAvailable, "Data store not available");

        var id = await NewProductAsync(jhb: 0);

        var level = await Inventory.AdjustAsync(new StockAdjustment { ProductId = id, WarehouseId = Cpt, MovementType = MovementType.Receipt, QuantityChange = 25, Reference = "PO-1" }, User);

        Assert.Equal(25, level.QuantityOnHand);
        Assert.Equal("CPT", level.WarehouseCode);
        var movement = Assert.Single(await Products.GetMovementsAsync(id, 10));
        Assert.Equal(MovementType.Receipt, movement.MovementType);
        Assert.Equal(25, movement.QuantityChange);
        Assert.Equal("PO-1", movement.Reference);
        Assert.Equal(User, movement.CreatedBy);
    }

    [SkippableFact]
    public async Task Adjustment_WithoutReason_IsRejected()
    {
        Skip.IfNot(IsAvailable, "Data store not available");

        var id = await NewProductAsync(jhb: 10);

        await Assert.ThrowsAsync<BusinessValidationException>(() =>
            Inventory.AdjustAsync(new StockAdjustment { ProductId = id, WarehouseId = Jhb, MovementType = MovementType.Adjustment, QuantityChange = -1 }, User));
    }

    [SkippableFact]
    public async Task Adjustment_CannotReduceStockBelowWhatIsReserved()
    {
        Skip.IfNot(IsAvailable, "Data store not available");

        var id = await NewProductAsync(jhb: 10);
        var order = await NewOrderAsync((id, 6));
        await Orders.AllocateAsync(order, User);

        await Assert.ThrowsAsync<InsufficientStockException>(() =>
            Inventory.AdjustAsync(new StockAdjustment { ProductId = id, WarehouseId = Jhb, MovementType = MovementType.Adjustment, QuantityChange = -5, Reason = "Damaged" }, User));

        var level = await Inventory.AdjustAsync(new StockAdjustment { ProductId = id, WarehouseId = Jhb, MovementType = MovementType.Adjustment, QuantityChange = -4, Reason = "Damaged" }, User);
        Assert.Equal(6, level.QuantityOnHand);
        Assert.Equal(0, level.QuantityAvailable);
    }

    [SkippableFact]
    public async Task Order_FullLifecycle_MovesStockCorrectly()
    {
        Skip.IfNot(IsAvailable, "Data store not available");

        var a = await NewProductAsync(jhb: 20, price: 10m);
        var b = await NewProductAsync(jhb: 5, price: 2.5m);
        var orderId = await NewOrderAsync((a, 8), (b, 5));

        var created = (await Orders.GetByIdAsync(orderId))!;
        Assert.Equal(OrderStatus.Open, created.Status);
        Assert.Matches(@"^SO-\d{6}$", created.OrderNumber);
        Assert.Equal(92.5m, created.TotalValue);

        await Orders.AllocateAsync(orderId, User);
        Assert.Equal(OrderStatus.Allocated, (await Orders.GetByIdAsync(orderId))!.Status);
        await AssertStockAsync(a, onHand: 20, reserved: 8);
        await AssertStockAsync(b, onHand: 5, reserved: 5);

        await Orders.DispatchAsync(orderId, User);
        var dispatched = (await Orders.GetByIdAsync(orderId))!;
        Assert.Equal(OrderStatus.Dispatched, dispatched.Status);
        Assert.NotNull(dispatched.DispatchedAt);
        await AssertStockAsync(a, onHand: 12, reserved: 0);
        await AssertStockAsync(b, onHand: 0, reserved: 0);

        var movements = await Products.GetMovementsAsync(a, 10);
        Assert.Equal(new[] { MovementType.Dispatch, MovementType.Allocation, MovementType.Receipt }, movements.Select(m => m.MovementType));
    }

    [SkippableFact]
    public async Task Allocate_IsAllOrNothing()
    {
        Skip.IfNot(IsAvailable, "Data store not available");

        var plenty = await NewProductAsync(jhb: 100);
        var scarce = await NewProductAsync(jhb: 3);
        var orderId = await NewOrderAsync((plenty, 10), (scarce, 4));

        var ex = await Assert.ThrowsAsync<InsufficientStockException>(() => Orders.AllocateAsync(orderId, User));

        Assert.Contains("requested 4, available 3", ex.Message, StringComparison.Ordinal);
        Assert.Equal(OrderStatus.Open, (await Orders.GetByIdAsync(orderId))!.Status);
        await AssertStockAsync(plenty, onHand: 100, reserved: 0);
    }

    [SkippableFact]
    public async Task Allocate_ProductNeverStockedInWarehouse_ReportsZeroAvailable()
    {
        Skip.IfNot(IsAvailable, "Data store not available");

        var id = await NewProductAsync(jhb: 0);
        var orderId = await NewOrderAsync((id, 1));

        var ex = await Assert.ThrowsAsync<InsufficientStockException>(() => Orders.AllocateAsync(orderId, User));
        Assert.Contains("available 0", ex.Message, StringComparison.Ordinal);
    }

    [SkippableFact]
    public async Task ConcurrentAllocations_NeverOversell()
    {
        Skip.IfNot(IsAvailable, "Data store not available");

        // Regression test for RCA-001: five orders race for stock that covers only two of them.
        var id = await NewProductAsync(jhb: 10);
        var orderIds = new List<int>();
        for (var i = 0; i < 5; i++)
        {
            orderIds.Add(await NewOrderAsync((id, 5)));
        }

        var results = await Task.WhenAll(orderIds.Select(async orderId =>
        {
            try
            {
                await Task.Yield();
                await Orders.AllocateAsync(orderId, User);
                return true;
            }
            catch (InsufficientStockException)
            {
                return false;
            }
        }));

        Assert.Equal(2, results.Count(r => r));
        await AssertStockAsync(id, onHand: 10, reserved: 10);
    }

    [SkippableFact]
    public async Task Cancel_AllocatedOrder_ReleasesTheReservation()
    {
        Skip.IfNot(IsAvailable, "Data store not available");

        var id = await NewProductAsync(jhb: 10);
        var orderId = await NewOrderAsync((id, 7));
        await Orders.AllocateAsync(orderId, User);

        await Orders.CancelAsync(orderId, User);

        Assert.Equal(OrderStatus.Cancelled, (await Orders.GetByIdAsync(orderId))!.Status);
        await AssertStockAsync(id, onHand: 10, reserved: 0);
        Assert.Equal(MovementType.Release, (await Products.GetMovementsAsync(id, 1)).Single().MovementType);
    }

    [SkippableFact]
    public async Task InvalidTransitions_AreRejected()
    {
        Skip.IfNot(IsAvailable, "Data store not available");

        var id = await NewProductAsync(jhb: 10);
        var orderId = await NewOrderAsync((id, 1));

        await Assert.ThrowsAsync<InvalidOrderStateException>(() => Orders.DispatchAsync(orderId, User));
        await Orders.CancelAsync(orderId, User);
        await Assert.ThrowsAsync<InvalidOrderStateException>(() => Orders.AllocateAsync(orderId, User));
        await Assert.ThrowsAsync<InvalidOrderStateException>(() => Orders.CancelAsync(orderId, User));
    }

    [SkippableFact]
    public async Task MissingOrder_ReturnsNullOrNotFound()
    {
        Skip.IfNot(IsAvailable, "Data store not available");

        Assert.Null(await Orders.GetByIdAsync(int.MaxValue));
        await Assert.ThrowsAsync<EntityNotFoundException>(() => Orders.AllocateAsync(int.MaxValue, User));
    }

    [SkippableFact]
    public async Task CreateOrder_WithInactiveProduct_IsRejected()
    {
        Skip.IfNot(IsAvailable, "Data store not available");

        var id = await NewProductAsync(jhb: 10);
        var product = (await Products.GetByIdAsync(id))!.Product;
        product.IsActive = false;
        await Products.SaveAsync(product);

        await Assert.ThrowsAsync<BusinessValidationException>(() => NewOrderAsync((id, 1)));
    }

    [SkippableFact]
    public async Task LowStockReport_IncludesProductsAtOrBelowReorderLevel()
    {
        Skip.IfNot(IsAvailable, "Data store not available");

        var low = await NewProductAsync(jhb: 4, reorderLevel: 5);
        var fine = await NewProductAsync(jhb: 6, reorderLevel: 5);

        var report = await Reports.GetLowStockAsync();

        var item = Assert.Single(report, r => r.ProductId == low);
        Assert.Equal(6, item.SuggestedOrderQuantity);
        Assert.DoesNotContain(report, r => r.ProductId == fine);

        var search = await Products.SearchAsync(new ProductQuery { LowStockOnly = true });
        Assert.Contains(search, p => p.ProductId == low);
        Assert.DoesNotContain(search, p => p.ProductId == fine);
    }

    // ------------------------------------------------------------------ helpers

    private static string NewSku() => ("T-" + Guid.NewGuid().ToString("N")[..12]).ToUpperInvariant();

    private async Task<int> NewProductAsync(int jhb, decimal price = 1m, int reorderLevel = 0)
    {
        var id = await Products.SaveAsync(new Product { Sku = NewSku(), Name = "Contract test item", CategoryId = 1, UnitPrice = price, ReorderLevel = reorderLevel });
        if (jhb > 0)
        {
            await Inventory.AdjustAsync(new StockAdjustment { ProductId = id, WarehouseId = Jhb, MovementType = MovementType.Receipt, QuantityChange = jhb }, User);
        }

        return id;
    }

    private Task<int> NewOrderAsync(params (int ProductId, int Quantity)[] lines) =>
        Orders.CreateAsync(new NewSalesOrder
        {
            CustomerId = Customer,
            WarehouseId = Jhb,
            Lines = lines.Select(l => new NewOrderLine(l.ProductId, l.Quantity)).ToList(),
        }, User);

    private async Task AssertStockAsync(int productId, int onHand, int reserved)
    {
        var jhb = (await Products.GetByIdAsync(productId))!.StockLevels.Single(s => s.WarehouseId == Jhb);
        Assert.Equal((onHand, reserved), (jhb.QuantityOnHand, jhb.QuantityReserved));
    }
}
