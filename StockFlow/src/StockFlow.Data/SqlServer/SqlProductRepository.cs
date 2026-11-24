using System.Data;
using Microsoft.Data.SqlClient;
using StockFlow.Core.Abstractions;
using StockFlow.Core.Models;

namespace StockFlow.Data.SqlServer;

public sealed class SqlProductRepository : IProductRepository
{
    private readonly SqlExecutor _sql;

    public SqlProductRepository(SqlExecutor sql) => _sql = sql ?? throw new ArgumentNullException(nameof(sql));

    public Task<IReadOnlyList<ProductSummary>> SearchAsync(ProductQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return _sql.QueryAsync<IReadOnlyList<ProductSummary>>(
            "dbo.usp_Product_Search",
            p =>
            {
                p.Add("@SearchTerm", SqlDbType.NVarChar, 100).Value = query.SearchTerm.ToDbValue();
                p.Add("@CategoryId", SqlDbType.Int).Value = (object?)query.CategoryId ?? DBNull.Value;
                p.Add("@LowStockOnly", SqlDbType.Bit).Value = query.LowStockOnly;
                p.Add("@IncludeInactive", SqlDbType.Bit).Value = query.IncludeInactive;
            },
            async (r, ct) => await r.ReadAllAsync(MapSummary, ct).ConfigureAwait(false),
            cancellationToken);
    }

    public Task<ProductDetail?> GetByIdAsync(int productId, CancellationToken cancellationToken = default)
    {
        return _sql.QueryAsync(
            "dbo.usp_Product_GetById",
            p => p.Add("@ProductId", SqlDbType.Int).Value = productId,
            async (r, ct) =>
            {
                if (!await r.ReadAsync(ct).ConfigureAwait(false))
                {
                    return null;
                }

                var product = new Product
                {
                    ProductId = r.GetInt32("ProductId"),
                    Sku = r.GetString("Sku"),
                    Name = r.GetString("Name"),
                    CategoryId = r.GetInt32("CategoryId"),
                    CategoryName = r.GetString("CategoryName"),
                    UnitPrice = r.GetDecimal("UnitPrice"),
                    ReorderLevel = r.GetInt32("ReorderLevel"),
                    IsActive = r.GetBoolean("IsActive"),
                    RowVersion = r.GetBytes("RowVer"),
                };

                await r.NextResultAsync(ct).ConfigureAwait(false);
                var stock = await r.ReadAllAsync(MapStockLevel, ct).ConfigureAwait(false);

                return (ProductDetail?)new ProductDetail(product, stock);
            },
            cancellationToken);
    }

    public Task<int> SaveAsync(Product product, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(product);

        return _sql.QueryAsync(
            "dbo.usp_Product_Save",
            p =>
            {
                p.Add("@ProductId", SqlDbType.Int).Value = product.IsNew ? DBNull.Value : product.ProductId;
                p.Add("@Sku", SqlDbType.NVarChar, 32).Value = product.Sku;
                p.Add("@Name", SqlDbType.NVarChar, 200).Value = product.Name;
                p.Add("@CategoryId", SqlDbType.Int).Value = product.CategoryId;
                var price = p.Add("@UnitPrice", SqlDbType.Decimal);
                price.Precision = 18;
                price.Scale = 2;
                price.Value = product.UnitPrice;
                p.Add("@ReorderLevel", SqlDbType.Int).Value = product.ReorderLevel;
                p.Add("@IsActive", SqlDbType.Bit).Value = product.IsActive;
                p.Add("@RowVersion", SqlDbType.Binary, 8).Value = (object?)product.RowVersion ?? DBNull.Value;
            },
            async (r, ct) =>
            {
                await r.ReadAsync(ct).ConfigureAwait(false);
                return r.GetInt32("ProductId");
            },
            cancellationToken);
    }

    public Task<IReadOnlyList<StockMovement>> GetMovementsAsync(int productId, int top, CancellationToken cancellationToken = default)
    {
        return _sql.QueryAsync<IReadOnlyList<StockMovement>>(
            "dbo.usp_StockMovement_ListByProduct",
            p =>
            {
                p.Add("@ProductId", SqlDbType.Int).Value = productId;
                p.Add("@Top", SqlDbType.Int).Value = top;
            },
            async (r, ct) => await r.ReadAllAsync(row => new StockMovement
            {
                StockMovementId = row.GetInt64("StockMovementId"),
                ProductId = row.GetInt32("ProductId"),
                WarehouseId = row.GetInt32("WarehouseId"),
                WarehouseCode = row.GetString("WarehouseCode"),
                MovementType = (MovementType)row.GetByte("MovementType"),
                QuantityChange = row.GetInt32("QuantityChange"),
                ReservedChange = row.GetInt32("ReservedChange"),
                Reference = row.GetNullableString("Reference"),
                Reason = row.GetNullableString("Reason"),
                CreatedBy = row.GetString("CreatedBy"),
                CreatedAt = row.GetDateTime("CreatedAt"),
            }, ct).ConfigureAwait(false),
            cancellationToken);
    }

    internal static StockLevel MapStockLevel(SqlDataReader r) => new()
    {
        ProductId = r.GetInt32("ProductId"),
        WarehouseId = r.GetInt32("WarehouseId"),
        WarehouseCode = r.GetString("WarehouseCode"),
        WarehouseName = r.GetString("WarehouseName"),
        QuantityOnHand = r.GetInt32("QuantityOnHand"),
        QuantityReserved = r.GetInt32("QuantityReserved"),
    };

    private static ProductSummary MapSummary(SqlDataReader r) => new()
    {
        ProductId = r.GetInt32("ProductId"),
        Sku = r.GetString("Sku"),
        Name = r.GetString("Name"),
        CategoryName = r.GetString("CategoryName"),
        UnitPrice = r.GetDecimal("UnitPrice"),
        ReorderLevel = r.GetInt32("ReorderLevel"),
        IsActive = r.GetBoolean("IsActive"),
        QuantityOnHand = r.GetInt32("QuantityOnHand"),
        QuantityReserved = r.GetInt32("QuantityReserved"),
    };
}
