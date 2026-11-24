using System.Data;
using StockFlow.Core.Abstractions;
using StockFlow.Core.Models;

namespace StockFlow.Data.SqlServer;

public sealed class SqlInventoryRepository : IInventoryRepository
{
    private readonly SqlExecutor _sql;

    public SqlInventoryRepository(SqlExecutor sql) => _sql = sql ?? throw new ArgumentNullException(nameof(sql));

    public Task<StockLevel> AdjustAsync(StockAdjustment adjustment, string user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(adjustment);

        return _sql.QueryAsync(
            "dbo.usp_Stock_Adjust",
            p =>
            {
                p.Add("@ProductId", SqlDbType.Int).Value = adjustment.ProductId;
                p.Add("@WarehouseId", SqlDbType.Int).Value = adjustment.WarehouseId;
                p.Add("@MovementType", SqlDbType.TinyInt).Value = (byte)adjustment.MovementType;
                p.Add("@QuantityChange", SqlDbType.Int).Value = adjustment.QuantityChange;
                p.Add("@Reference", SqlDbType.NVarChar, 50).Value = adjustment.Reference.ToDbValue();
                p.Add("@Reason", SqlDbType.NVarChar, 200).Value = adjustment.Reason.ToDbValue();
                p.Add("@User", SqlDbType.NVarChar, 100).Value = user;
            },
            async (r, ct) =>
            {
                await r.ReadAsync(ct).ConfigureAwait(false);
                return SqlProductRepository.MapStockLevel(r);
            },
            cancellationToken);
    }
}
