using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Data.SqlClient.Server;
using StockFlow.Core.Abstractions;
using StockFlow.Core.Models;

namespace StockFlow.Data.SqlServer;

public sealed class SqlOrderRepository : IOrderRepository
{
    private static readonly SqlMetaData[] OrderLineListSchema =
    {
        new("ProductId", SqlDbType.Int),
        new("Quantity", SqlDbType.Int),
    };

    private readonly SqlExecutor _sql;

    public SqlOrderRepository(SqlExecutor sql) => _sql = sql ?? throw new ArgumentNullException(nameof(sql));

    public Task<IReadOnlyList<SalesOrderSummary>> SearchAsync(OrderQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return _sql.QueryAsync<IReadOnlyList<SalesOrderSummary>>(
            "dbo.usp_Order_Search",
            p =>
            {
                p.Add("@Status", SqlDbType.TinyInt).Value = query.Status.HasValue ? (byte)query.Status.Value : DBNull.Value;
                p.Add("@CustomerId", SqlDbType.Int).Value = (object?)query.CustomerId ?? DBNull.Value;
                p.Add("@SearchTerm", SqlDbType.NVarChar, 100).Value = query.SearchTerm.ToDbValue();
                p.Add("@Top", SqlDbType.Int).Value = query.Top;
            },
            async (r, ct) => await r.ReadAllAsync(row => new SalesOrderSummary
            {
                SalesOrderId = row.GetInt32("SalesOrderId"),
                OrderNumber = row.GetString("OrderNumber"),
                CustomerId = row.GetInt32("CustomerId"),
                CustomerName = row.GetString("CustomerName"),
                WarehouseId = row.GetInt32("WarehouseId"),
                WarehouseCode = row.GetString("WarehouseCode"),
                Status = (OrderStatus)row.GetByte("Status"),
                OrderDate = row.GetDateTime("OrderDate"),
                RequiredDate = row.GetNullableDateTime("RequiredDate"),
                LineCount = row.GetInt32("LineCount"),
                TotalQuantity = row.GetInt32("TotalQuantity"),
                TotalValue = row.GetDecimal("TotalValue"),
            }, ct).ConfigureAwait(false),
            cancellationToken);
    }

    public Task<SalesOrder?> GetByIdAsync(int salesOrderId, CancellationToken cancellationToken = default)
    {
        return _sql.QueryAsync(
            "dbo.usp_Order_GetById",
            p => p.Add("@SalesOrderId", SqlDbType.Int).Value = salesOrderId,
            async (r, ct) =>
            {
                if (!await r.ReadAsync(ct).ConfigureAwait(false))
                {
                    return null;
                }

                var order = new SalesOrder
                {
                    SalesOrderId = r.GetInt32("SalesOrderId"),
                    OrderNumber = r.GetString("OrderNumber"),
                    CustomerId = r.GetInt32("CustomerId"),
                    CustomerName = r.GetString("CustomerName"),
                    WarehouseId = r.GetInt32("WarehouseId"),
                    WarehouseCode = r.GetString("WarehouseCode"),
                    Status = (OrderStatus)r.GetByte("Status"),
                    OrderDate = r.GetDateTime("OrderDate"),
                    RequiredDate = r.GetNullableDateTime("RequiredDate"),
                    Notes = r.GetNullableString("Notes"),
                    CreatedBy = r.GetString("CreatedBy"),
                    DispatchedAt = r.GetNullableDateTime("DispatchedAt"),
                };

                await r.NextResultAsync(ct).ConfigureAwait(false);
                order.Lines = await r.ReadAllAsync(row => new SalesOrderLine
                {
                    SalesOrderLineId = row.GetInt32("SalesOrderLineId"),
                    ProductId = row.GetInt32("ProductId"),
                    Sku = row.GetString("Sku"),
                    ProductName = row.GetString("ProductName"),
                    Quantity = row.GetInt32("Quantity"),
                    UnitPrice = row.GetDecimal("UnitPrice"),
                    QuantityAvailable = row.GetInt32("QuantityAvailable"),
                }, ct).ConfigureAwait(false);

                return (SalesOrder?)order;
            },
            cancellationToken);
    }

    public Task<int> CreateAsync(NewSalesOrder order, string user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);

        return _sql.QueryAsync(
            "dbo.usp_Order_Create",
            p =>
            {
                p.Add("@CustomerId", SqlDbType.Int).Value = order.CustomerId;
                p.Add("@WarehouseId", SqlDbType.Int).Value = order.WarehouseId;
                p.Add("@RequiredDate", SqlDbType.Date).Value = (object?)order.RequiredDate?.Date ?? DBNull.Value;
                p.Add("@Notes", SqlDbType.NVarChar, 500).Value = order.Notes.ToDbValue();
                p.Add("@User", SqlDbType.NVarChar, 100).Value = user;

                var lines = p.Add("@Lines", SqlDbType.Structured);
                lines.TypeName = "dbo.OrderLineList";
                lines.Value = ToRecords(order.Lines);
            },
            async (r, ct) =>
            {
                await r.ReadAsync(ct).ConfigureAwait(false);
                return r.GetInt32("SalesOrderId");
            },
            cancellationToken);
    }

    public Task AllocateAsync(int salesOrderId, string user, CancellationToken cancellationToken = default) =>
        ChangeStatusAsync("dbo.usp_Order_Allocate", salesOrderId, user, cancellationToken);

    public Task DispatchAsync(int salesOrderId, string user, CancellationToken cancellationToken = default) =>
        ChangeStatusAsync("dbo.usp_Order_Dispatch", salesOrderId, user, cancellationToken);

    public Task CancelAsync(int salesOrderId, string user, CancellationToken cancellationToken = default) =>
        ChangeStatusAsync("dbo.usp_Order_Cancel", salesOrderId, user, cancellationToken);

    private Task ChangeStatusAsync(string procedure, int salesOrderId, string user, CancellationToken cancellationToken)
    {
        return _sql.ExecuteAsync(
            procedure,
            p =>
            {
                p.Add("@SalesOrderId", SqlDbType.Int).Value = salesOrderId;
                p.Add("@User", SqlDbType.NVarChar, 100).Value = user;
            },
            cancellationToken);
    }

    /// <summary>
    /// Table-valued parameter rows. An empty TVP must be sent as null
    /// (ADO.NET rejects an empty record list), which SQL Server treats as an empty table.
    /// </summary>
    private static IEnumerable<SqlDataRecord>? ToRecords(IReadOnlyCollection<NewOrderLine> lines)
    {
        if (lines.Count == 0)
        {
            return null;
        }

        return lines.Select(line =>
        {
            var record = new SqlDataRecord(OrderLineListSchema);
            record.SetInt32(0, line.ProductId);
            record.SetInt32(1, line.Quantity);
            return record;
        }).ToList();
    }
}
