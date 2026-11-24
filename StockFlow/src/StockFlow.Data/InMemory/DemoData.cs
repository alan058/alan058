using StockFlow.Core.Abstractions;
using StockFlow.Core.Models;

namespace StockFlow.Data.InMemory;

/// <summary>
/// The same reference and demo data as database/migrations/0003_ReferenceData.sql
/// and database/seed/0001_DemoData.sql, so both data providers look identical.
/// </summary>
internal static class DemoData
{
    private const string SeedUser = "seed";

    public static void LoadReferenceData(InMemoryStockFlowStore store)
    {
        store.AddCategory(1, "Audio");
        store.AddCategory(2, "Computing");
        store.AddCategory(3, "Mobile Accessories");
        store.AddCategory(4, "Smart Home");
        store.AddCategory(5, "Gaming");

        store.AddWarehouse(1, "JHB", "Johannesburg Distribution Centre");
        store.AddWarehouse(2, "CPT", "Cape Town Depot");
        store.AddWarehouse(3, "DBN", "Durban Depot");
    }

    public static void Load(InMemoryStockFlowStore store)
    {
        LoadReferenceData(store);

        store.AddCustomer(1, "GHR001", "Gadget Hub Retail");
        store.AddCustomer(2, "NGE001", "Northgate Electronics");
        store.AddCustomer(3, "CTS001", "Coastal Tech Supplies");
        store.AddCustomer(4, "HOS001", "Highveld Office Solutions");
        store.AddCustomer(5, "BSS001", "Bright Spark Stores");
        store.AddCustomer(6, "MMK001", "Metro Mobile Kiosks");

        // (sku, name, category, price, reorder level, JHB, CPT, DBN)
        var products = new (string Sku, string Name, int Category, decimal Price, int Reorder, int Jhb, int Cpt, int Dbn)[]
        {
            ("AUD-001", "Wireless Earbuds Pro",            1, 1299.00m,  40, 120,  35, 20),
            ("AUD-002", "Bluetooth Speaker Mini",          1,  649.00m,  30,  18,  10,  0),
            ("AUD-003", "Noise Cancelling Headphones",     1, 3499.00m,  15,  25,   8,  4),
            ("CMP-001", "USB-C Docking Station",           2, 1899.00m,  20,  40,  12,  6),
            ("CMP-002", "Wireless Keyboard and Mouse Set", 2,  799.00m,  25,  60,  15, 10),
            ("CMP-003", "27-inch 4K Monitor",              2, 6499.00m,   8,   5,   2,  0),
            ("CMP-004", "1TB Portable SSD",                2, 1599.00m,  20,  45,  20, 10),
            ("MOB-001", "65W GaN Fast Charger",            3,  499.00m,  60, 200,  80, 50),
            ("MOB-002", "USB-C to Lightning Cable 1m",     3,  199.00m, 100,  70,  20,  5),
            ("MOB-003", "10000mAh Power Bank",             3,  449.00m,  50,  90,  40, 25),
            ("MOB-004", "MagSafe Phone Case",              3,  349.00m,  40,  55,  25, 12),
            ("SHM-001", "Smart Plug (2-pack)",             4,  399.00m,  30,  35,  10,  8),
            ("SHM-002", "Wi-Fi Security Camera",           4, 1199.00m,  15,   9,   3,  0),
            ("SHM-003", "Smart LED Bulb E27",              4,  149.00m,  80, 300, 120, 60),
            ("GAM-001", "Wireless Game Controller",        5,  999.00m,  20,  30,  12,  6),
            ("GAM-002", "Gaming Headset RGB",              5, 1499.00m,  12,  14,   4,  2),
        };

        foreach (var p in products)
        {
            var id = store.SaveAsync(new Product
            {
                Sku = p.Sku,
                Name = p.Name,
                CategoryId = p.Category,
                UnitPrice = p.Price,
                ReorderLevel = p.Reorder,
            }).GetAwaiter().GetResult();

            Receive(store, id, 1, p.Jhb);
            Receive(store, id, 2, p.Cpt);
            Receive(store, id, 3, p.Dbn);
        }

        IOrderRepository orders = store;
        var first = CreateOrder(orders, 1, 1, null, (1, 10), (8, 20));
        orders.AllocateAsync(first, SeedUser).GetAwaiter().GetResult();

        CreateOrder(orders, 2, 1, null, (4, 5), (5, 5));

        var third = CreateOrder(orders, 3, 3, null, (10, 5), (14, 20));
        orders.AllocateAsync(third, SeedUser).GetAwaiter().GetResult();
        orders.DispatchAsync(third, SeedUser).GetAwaiter().GetResult();

        CreateOrder(orders, 4, 2, "Deliver to reception", (6, 2));
        CreateOrder(orders, 5, 1, "Promotion stock - more than JHB holds", (2, 30));
    }

    private static void Receive(InMemoryStockFlowStore store, int productId, int warehouseId, int quantity)
    {
        if (quantity <= 0)
        {
            return;
        }

        store.AdjustAsync(new StockAdjustment
        {
            ProductId = productId,
            WarehouseId = warehouseId,
            MovementType = MovementType.Receipt,
            QuantityChange = quantity,
            Reference = "OPENING",
            Reason = "Opening balance",
        }, SeedUser).GetAwaiter().GetResult();
    }

    private static int CreateOrder(IOrderRepository orders, int customerId, int warehouseId, string? notes, params (int ProductId, int Quantity)[] lines)
    {
        return orders.CreateAsync(new NewSalesOrder
        {
            CustomerId = customerId,
            WarehouseId = warehouseId,
            Notes = notes,
            Lines = lines.Select(l => new NewOrderLine(l.ProductId, l.Quantity)).ToList(),
        }, SeedUser).GetAwaiter().GetResult();
    }
}
