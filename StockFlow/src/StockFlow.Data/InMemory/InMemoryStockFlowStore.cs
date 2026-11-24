using StockFlow.Core.Abstractions;
using StockFlow.Core.Exceptions;
using StockFlow.Core.Models;

namespace StockFlow.Data.InMemory;

/// <summary>
/// A thread-safe, in-process implementation of every repository with the same
/// business rules as the stored procedures. Used for demos (no SQL Server
/// needed) and fast tests. The repository contract tests run against both this
/// and SQL Server to keep the two in step.
///
/// Objects are copied on the way in and out so callers can never change the
/// store's state by accident through a shared reference.
/// </summary>
public sealed class InMemoryStockFlowStore : IProductRepository, IInventoryRepository, IOrderRepository, IReportRepository
{
    private readonly object _gate = new();
    private readonly List<LookupItem> _categories = new();
    private readonly List<LookupItem> _warehouses = new();
    private readonly List<LookupItem> _customers = new();
    private readonly Dictionary<int, Product> _products = new();
    private readonly Dictionary<(int ProductId, int WarehouseId), (int OnHand, int Reserved)> _stock = new();
    private readonly Dictionary<int, StoredOrder> _orders = new();
    private readonly List<StockMovement> _movements = new();
    private int _nextProductId = 1;
    private int _nextOrderId = 1;
    private int _nextOrderLineId = 1;
    private long _nextMovementId = 1;
    private long _rowVersion = 1;

    public static InMemoryStockFlowStore CreateWithDemoData()
    {
        var store = new InMemoryStockFlowStore();
        DemoData.Load(store);
        return store;
    }

    /// <summary>Creates a store with only the reference data (categories and warehouses).</summary>
    public static InMemoryStockFlowStore CreateWithReferenceData()
    {
        var store = new InMemoryStockFlowStore();
        DemoData.LoadReferenceData(store);
        return store;
    }

    internal void AddCategory(int id, string name) => _categories.Add(new LookupItem(id, null, name));

    internal void AddWarehouse(int id, string code, string name) => _warehouses.Add(new LookupItem(id, code, name));

    internal void AddCustomer(int id, string code, string name) => _customers.Add(new LookupItem(id, code, name));

    // ----------------------------------------------------------------- Products

    public Task<IReadOnlyList<ProductSummary>> SearchAsync(ProductQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var term = string.IsNullOrWhiteSpace(query.SearchTerm) ? null : query.SearchTerm.Trim();

        lock (_gate)
        {
            IReadOnlyList<ProductSummary> results = _products.Values
                .Where(p => term is null
                            || p.Sku.Contains(term, StringComparison.OrdinalIgnoreCase)
                            || p.Name.Contains(term, StringComparison.OrdinalIgnoreCase))
                .Where(p => query.CategoryId is null || p.CategoryId == query.CategoryId)
                .Where(p => query.IncludeInactive || p.IsActive)
                .Select(ToSummary)
                .Where(s => !query.LowStockOnly || s.IsLowStock)
                .OrderBy(s => s.Sku, StringComparer.Ordinal)
                .ToList();
            return Task.FromResult(results);
        }
    }

    public Task<ProductDetail?> GetByIdAsync(int productId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!_products.TryGetValue(productId, out var product))
            {
                return Task.FromResult<ProductDetail?>(null);
            }

            var levels = _warehouses
                .OrderBy(w => w.Code, StringComparer.Ordinal)
                .Select(w => ToStockLevel(productId, w))
                .ToList();

            return Task.FromResult<ProductDetail?>(new ProductDetail(Copy(product), levels));
        }
    }

    public Task<int> SaveAsync(Product product, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(product);

        lock (_gate)
        {
            var category = _categories.FirstOrDefault(c => c.Id == product.CategoryId)
                ?? throw new BusinessValidationException("Category does not exist.");

            if (_products.Values.Any(p => string.Equals(p.Sku, product.Sku, StringComparison.OrdinalIgnoreCase) && p.ProductId != product.ProductId))
            {
                throw new BusinessValidationException("Another product already uses this SKU.");
            }

            Product stored;
            if (product.IsNew)
            {
                stored = new Product { ProductId = _nextProductId++ };
                _products[stored.ProductId] = stored;
            }
            else
            {
                if (!_products.TryGetValue(product.ProductId, out stored!))
                {
                    throw new EntityNotFoundException("Product not found.");
                }

                if (product.RowVersion is not null && !product.RowVersion.AsSpan().SequenceEqual(stored.RowVersion))
                {
                    throw new ConcurrencyException("This product was changed by another user. Reload it and try again.");
                }
            }

            stored.Sku = product.Sku;
            stored.Name = product.Name;
            stored.CategoryId = category.Id;
            stored.CategoryName = category.Name;
            stored.UnitPrice = product.UnitPrice;
            stored.ReorderLevel = product.ReorderLevel;
            stored.IsActive = product.IsActive;
            stored.RowVersion = BitConverter.GetBytes(_rowVersion++);

            return Task.FromResult(stored.ProductId);
        }
    }

    public Task<IReadOnlyList<StockMovement>> GetMovementsAsync(int productId, int top, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            IReadOnlyList<StockMovement> results = _movements
                .Where(m => m.ProductId == productId)
                .OrderByDescending(m => m.CreatedAt)
                .ThenByDescending(m => m.StockMovementId)
                .Take(top)
                .Select(Copy)
                .ToList();
            return Task.FromResult(results);
        }
    }

    // ---------------------------------------------------------------- Inventory

    public Task<StockLevel> AdjustAsync(StockAdjustment adjustment, string user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(adjustment);

        if (adjustment.MovementType is not (MovementType.Receipt or MovementType.Adjustment))
        {
            throw new BusinessValidationException("Only receipts and adjustments can be posted directly.");
        }

        if (adjustment.QuantityChange == 0 || (adjustment.MovementType == MovementType.Receipt && adjustment.QuantityChange < 0))
        {
            throw new BusinessValidationException("Quantity must be positive for a receipt and non-zero for an adjustment.");
        }

        if (adjustment.MovementType == MovementType.Adjustment && string.IsNullOrWhiteSpace(adjustment.Reason))
        {
            throw new BusinessValidationException("A reason is required for a stock adjustment.");
        }

        lock (_gate)
        {
            if (!_products.ContainsKey(adjustment.ProductId))
            {
                throw new EntityNotFoundException("Product not found.");
            }

            var warehouse = _warehouses.FirstOrDefault(w => w.Id == adjustment.WarehouseId)
                ?? throw new EntityNotFoundException("Warehouse not found or inactive.");

            var key = (adjustment.ProductId, adjustment.WarehouseId);
            var (onHand, reserved) = _stock.GetValueOrDefault(key);

            if (onHand + adjustment.QuantityChange < reserved)
            {
                throw new InsufficientStockException(
                    $"Adjustment would leave {onHand + adjustment.QuantityChange} on hand but {reserved} are reserved for orders. " +
                    $"Available to adjust: {onHand - reserved}.");
            }

            _stock[key] = (onHand + adjustment.QuantityChange, reserved);
            AddMovement(adjustment.ProductId, adjustment.WarehouseId, adjustment.MovementType, adjustment.QuantityChange, 0, adjustment.Reference, adjustment.Reason, user);

            return Task.FromResult(ToStockLevel(adjustment.ProductId, warehouse));
        }
    }

    // ------------------------------------------------------------------- Orders

    public Task<IReadOnlyList<SalesOrderSummary>> SearchAsync(OrderQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var term = string.IsNullOrWhiteSpace(query.SearchTerm) ? null : query.SearchTerm.Trim();

        lock (_gate)
        {
            IReadOnlyList<SalesOrderSummary> results = _orders.Values
                .Where(o => query.Status is null || o.Status == query.Status)
                .Where(o => query.CustomerId is null || o.CustomerId == query.CustomerId)
                .Select(o => (Order: o, Customer: _customers.First(c => c.Id == o.CustomerId)))
                .Where(x => term is null
                            || x.Order.OrderNumber.Contains(term, StringComparison.OrdinalIgnoreCase)
                            || x.Customer.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                            || (x.Customer.Code ?? string.Empty).Contains(term, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.Order.OrderDate)
                .ThenByDescending(x => x.Order.SalesOrderId)
                .Take(query.Top)
                .Select(x => new SalesOrderSummary
                {
                    SalesOrderId = x.Order.SalesOrderId,
                    OrderNumber = x.Order.OrderNumber,
                    CustomerId = x.Order.CustomerId,
                    CustomerName = x.Customer.Name,
                    WarehouseId = x.Order.WarehouseId,
                    WarehouseCode = WarehouseCode(x.Order.WarehouseId),
                    Status = x.Order.Status,
                    OrderDate = x.Order.OrderDate,
                    RequiredDate = x.Order.RequiredDate,
                    LineCount = x.Order.Lines.Count,
                    TotalQuantity = x.Order.Lines.Sum(l => l.Quantity),
                    TotalValue = x.Order.Lines.Sum(l => l.Quantity * l.UnitPrice),
                })
                .ToList();
            return Task.FromResult(results);
        }
    }

    Task<SalesOrder?> IOrderRepository.GetByIdAsync(int salesOrderId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!_orders.TryGetValue(salesOrderId, out var o))
            {
                return Task.FromResult<SalesOrder?>(null);
            }

            var order = new SalesOrder
            {
                SalesOrderId = o.SalesOrderId,
                OrderNumber = o.OrderNumber,
                CustomerId = o.CustomerId,
                CustomerName = _customers.First(c => c.Id == o.CustomerId).Name,
                WarehouseId = o.WarehouseId,
                WarehouseCode = WarehouseCode(o.WarehouseId),
                Status = o.Status,
                OrderDate = o.OrderDate,
                RequiredDate = o.RequiredDate,
                Notes = o.Notes,
                CreatedBy = o.CreatedBy,
                DispatchedAt = o.DispatchedAt,
                Lines = o.Lines
                    .Select(l =>
                    {
                        var product = _products[l.ProductId];
                        var (onHand, reserved) = _stock.GetValueOrDefault((l.ProductId, o.WarehouseId));
                        return new SalesOrderLine
                        {
                            SalesOrderLineId = l.SalesOrderLineId,
                            ProductId = l.ProductId,
                            Sku = product.Sku,
                            ProductName = product.Name,
                            Quantity = l.Quantity,
                            UnitPrice = l.UnitPrice,
                            QuantityAvailable = onHand - reserved,
                        };
                    })
                    .OrderBy(l => l.Sku, StringComparer.Ordinal)
                    .ToList(),
            };

            return Task.FromResult<SalesOrder?>(order);
        }
    }

    public Task<int> CreateAsync(NewSalesOrder order, string user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);

        if (order.Lines.Count == 0)
        {
            throw new BusinessValidationException("An order needs at least one line.");
        }

        if (order.Lines.Any(l => l.Quantity <= 0))
        {
            throw new BusinessValidationException("Every line quantity must be greater than zero.");
        }

        if (order.Lines.GroupBy(l => l.ProductId).Any(g => g.Count() > 1))
        {
            // The TVP has ProductId as its primary key, so SQL Server rejects duplicates too.
            throw new BusinessValidationException("A record with the same key already exists.");
        }

        lock (_gate)
        {
            if (_customers.All(c => c.Id != order.CustomerId))
            {
                throw new EntityNotFoundException("Customer not found or inactive.");
            }

            if (_warehouses.All(w => w.Id != order.WarehouseId))
            {
                throw new EntityNotFoundException("Warehouse not found or inactive.");
            }

            if (order.Lines.Any(l => !_products.TryGetValue(l.ProductId, out var p) || !p.IsActive))
            {
                throw new BusinessValidationException("One or more products do not exist or are inactive.");
            }

            var id = _nextOrderId++;
            _orders[id] = new StoredOrder
            {
                SalesOrderId = id,
                OrderNumber = $"SO-{id:000000}",
                CustomerId = order.CustomerId,
                WarehouseId = order.WarehouseId,
                Status = OrderStatus.Open,
                OrderDate = DateTime.UtcNow,
                RequiredDate = order.RequiredDate?.Date,
                Notes = order.Notes,
                CreatedBy = user,
                Lines = order.Lines.Select(l => new StoredOrderLine
                {
                    SalesOrderLineId = _nextOrderLineId++,
                    ProductId = l.ProductId,
                    Quantity = l.Quantity,
                    UnitPrice = _products[l.ProductId].UnitPrice,
                }).ToList(),
            };

            return Task.FromResult(id);
        }
    }

    public Task AllocateAsync(int salesOrderId, string user, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var order = GetOrderForUpdate(salesOrderId);
            if (order.Status != OrderStatus.Open)
            {
                throw new InvalidOrderStateException("Only open orders can be allocated.");
            }

            var shortages = order.Lines
                .Select(l =>
                {
                    var (onHand, reserved) = _stock.GetValueOrDefault((l.ProductId, order.WarehouseId));
                    return (Line: l, Available: onHand - reserved);
                })
                .Where(x => x.Available < x.Line.Quantity)
                .Select(x => $"{_products[x.Line.ProductId].Sku} (requested {x.Line.Quantity}, available {x.Available})")
                .ToList();

            if (shortages.Count > 0)
            {
                throw new InsufficientStockException(
                    $"Insufficient stock to allocate {order.OrderNumber}: {string.Join("; ", shortages)}");
            }

            foreach (var line in order.Lines)
            {
                var key = (line.ProductId, order.WarehouseId);
                var (onHand, reserved) = _stock.GetValueOrDefault(key);
                _stock[key] = (onHand, reserved + line.Quantity);
                AddMovement(line.ProductId, order.WarehouseId, MovementType.Allocation, 0, line.Quantity, order.OrderNumber, null, user);
            }

            order.Status = OrderStatus.Allocated;
        }

        return Task.CompletedTask;
    }

    public Task DispatchAsync(int salesOrderId, string user, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var order = GetOrderForUpdate(salesOrderId);
            if (order.Status != OrderStatus.Allocated)
            {
                throw new InvalidOrderStateException("Only allocated orders can be dispatched.");
            }

            foreach (var line in order.Lines)
            {
                var key = (line.ProductId, order.WarehouseId);
                var (onHand, reserved) = _stock[key];
                _stock[key] = (onHand - line.Quantity, reserved - line.Quantity);
                AddMovement(line.ProductId, order.WarehouseId, MovementType.Dispatch, -line.Quantity, -line.Quantity, order.OrderNumber, null, user);
            }

            order.Status = OrderStatus.Dispatched;
            order.DispatchedAt = DateTime.UtcNow;
        }

        return Task.CompletedTask;
    }

    public Task CancelAsync(int salesOrderId, string user, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var order = GetOrderForUpdate(salesOrderId);
            if (order.Status is not (OrderStatus.Open or OrderStatus.Allocated))
            {
                throw new InvalidOrderStateException("Only open or allocated orders can be cancelled.");
            }

            if (order.Status == OrderStatus.Allocated)
            {
                foreach (var line in order.Lines)
                {
                    var key = (line.ProductId, order.WarehouseId);
                    var (onHand, reserved) = _stock[key];
                    _stock[key] = (onHand, reserved - line.Quantity);
                    AddMovement(line.ProductId, order.WarehouseId, MovementType.Release, 0, -line.Quantity, order.OrderNumber, null, user);
                }
            }

            order.Status = OrderStatus.Cancelled;
        }

        return Task.CompletedTask;
    }

    // ------------------------------------------------------------------ Reports

    public Task<DashboardSummary> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var active = _products.Values.Where(p => p.IsActive).Select(ToSummary).ToList();
            var today = DateTime.UtcNow.Date;

            return Task.FromResult(new DashboardSummary
            {
                ActiveProducts = active.Count,
                LowStockProducts = active.Count(p => p.IsLowStock),
                StockValue = active.Sum(p => p.QuantityOnHand * p.UnitPrice),
                OpenOrders = _orders.Values.Count(o => o.Status == OrderStatus.Open),
                AllocatedOrders = _orders.Values.Count(o => o.Status == OrderStatus.Allocated),
                DispatchedToday = _orders.Values.Count(o => o.Status == OrderStatus.Dispatched && o.DispatchedAt >= today),
            });
        }
    }

    public Task<IReadOnlyList<LowStockItem>> GetLowStockAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            IReadOnlyList<LowStockItem> results = _products.Values
                .Where(p => p.IsActive)
                .Select(ToSummary)
                .Where(s => s.IsLowStock)
                .OrderBy(s => s.QuantityAvailable - s.ReorderLevel)
                .ThenBy(s => s.Sku, StringComparer.Ordinal)
                .Select(s => new LowStockItem
                {
                    ProductId = s.ProductId,
                    Sku = s.Sku,
                    Name = s.Name,
                    CategoryName = s.CategoryName,
                    ReorderLevel = s.ReorderLevel,
                    QuantityOnHand = s.QuantityOnHand,
                    QuantityReserved = s.QuantityReserved,
                    QuantityAvailable = s.QuantityAvailable,
                    SuggestedOrderQuantity = (s.ReorderLevel * 2) - s.QuantityAvailable,
                })
                .ToList();
            return Task.FromResult(results);
        }
    }

    public Task<Lookups> GetLookupsAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(new Lookups
            {
                Categories = _categories.OrderBy(c => c.Name, StringComparer.Ordinal).Select(Copy).ToList(),
                Warehouses = _warehouses.OrderBy(w => w.Code, StringComparer.Ordinal).Select(Copy).ToList(),
                Customers = _customers.OrderBy(c => c.Name, StringComparer.Ordinal).Select(Copy).ToList(),
            });
        }
    }

    public Task<bool> PingAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

    // ------------------------------------------------------------------ Helpers

    private StoredOrder GetOrderForUpdate(int salesOrderId) =>
        _orders.TryGetValue(salesOrderId, out var order)
            ? order
            : throw new EntityNotFoundException("Sales order not found.");

    private string WarehouseCode(int warehouseId) => _warehouses.First(w => w.Id == warehouseId).Code ?? string.Empty;

    private ProductSummary ToSummary(Product p)
    {
        var levels = _stock.Where(s => s.Key.ProductId == p.ProductId).Select(s => s.Value).ToList();
        return new ProductSummary
        {
            ProductId = p.ProductId,
            Sku = p.Sku,
            Name = p.Name,
            CategoryName = p.CategoryName,
            UnitPrice = p.UnitPrice,
            ReorderLevel = p.ReorderLevel,
            IsActive = p.IsActive,
            QuantityOnHand = levels.Sum(l => l.OnHand),
            QuantityReserved = levels.Sum(l => l.Reserved),
        };
    }

    private StockLevel ToStockLevel(int productId, LookupItem warehouse)
    {
        var (onHand, reserved) = _stock.GetValueOrDefault((productId, warehouse.Id));
        return new StockLevel
        {
            ProductId = productId,
            WarehouseId = warehouse.Id,
            WarehouseCode = warehouse.Code ?? string.Empty,
            WarehouseName = warehouse.Name,
            QuantityOnHand = onHand,
            QuantityReserved = reserved,
        };
    }

    private void AddMovement(int productId, int warehouseId, MovementType type, int quantityChange, int reservedChange, string? reference, string? reason, string user)
    {
        _movements.Add(new StockMovement
        {
            StockMovementId = _nextMovementId++,
            ProductId = productId,
            WarehouseId = warehouseId,
            WarehouseCode = WarehouseCode(warehouseId),
            MovementType = type,
            QuantityChange = quantityChange,
            ReservedChange = reservedChange,
            Reference = reference,
            Reason = reason,
            CreatedBy = user,
            CreatedAt = DateTime.UtcNow,
        });
    }

    private static Product Copy(Product p) => new()
    {
        ProductId = p.ProductId,
        Sku = p.Sku,
        Name = p.Name,
        CategoryId = p.CategoryId,
        CategoryName = p.CategoryName,
        UnitPrice = p.UnitPrice,
        ReorderLevel = p.ReorderLevel,
        IsActive = p.IsActive,
        RowVersion = p.RowVersion?.ToArray(),
    };

    private static StockMovement Copy(StockMovement m) => new()
    {
        StockMovementId = m.StockMovementId,
        ProductId = m.ProductId,
        WarehouseId = m.WarehouseId,
        WarehouseCode = m.WarehouseCode,
        MovementType = m.MovementType,
        QuantityChange = m.QuantityChange,
        ReservedChange = m.ReservedChange,
        Reference = m.Reference,
        Reason = m.Reason,
        CreatedBy = m.CreatedBy,
        CreatedAt = m.CreatedAt,
    };

    private static LookupItem Copy(LookupItem i) => new(i.Id, i.Code, i.Name);

    private sealed class StoredOrder
    {
        public int SalesOrderId { get; init; }
        public string OrderNumber { get; init; } = string.Empty;
        public int CustomerId { get; init; }
        public int WarehouseId { get; init; }
        public OrderStatus Status { get; set; }
        public DateTime OrderDate { get; init; }
        public DateTime? RequiredDate { get; init; }
        public string? Notes { get; init; }
        public string CreatedBy { get; init; } = string.Empty;
        public DateTime? DispatchedAt { get; set; }
        public List<StoredOrderLine> Lines { get; init; } = new();
    }

    private sealed class StoredOrderLine
    {
        public int SalesOrderLineId { get; init; }
        public int ProductId { get; init; }
        public int Quantity { get; init; }
        public decimal UnitPrice { get; init; }
    }
}
