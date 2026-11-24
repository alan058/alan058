# Support runbook

How to investigate common StockFlow problems. Start with **where the logs are**,
then find the symptom below.

## Where to look first

| Source | Location | Find an incident by |
|---|---|---|
| Desktop app log | `%LOCALAPPDATA%\StockFlow\Logs\stockflow-desktop-YYYYMMDD.log` (*File → Open log folder* in the app) | The 8-character reference in the error message, e.g. `[3F9A1C2B]` |
| Web app log | stdout / the hosting platform's log stream | The reference on the error page = the `X-Correlation-ID` response header |
| Database errors | `SELECT TOP 50 * FROM dbo.ErrorLog ORDER BY ErrorLogId DESC;` | Time and procedure name |
| What changed stock, and who | `dbo.StockMovement` | Product, warehouse, time, `CreatedBy` |
| Which database version is deployed | `SELECT * FROM dbo.SchemaVersion ORDER BY AppliedAt;` | Script name |
| Is the web app connected? | `GET /health`, which returns `Healthy` or `Unhealthy` | — |

## Symptoms

### "Insufficient stock to allocate SO-xxxxxx: ..."

**Expected behaviour, not a defect.** The message lists each short line and how
many are available in the order's warehouse.

1. Check the product's stock by warehouse (web: *Products → product*). The order
   might ship from a warehouse that has none while another has plenty.
2. Check whether the stock is reserved by other allocated orders:

   ```sql
   SELECT o.OrderNumber, o.Status, l.Quantity
   FROM dbo.SalesOrderLine l
   JOIN dbo.SalesOrder o ON o.SalesOrderId = l.SalesOrderId
   WHERE l.ProductId = @ProductId AND o.WarehouseId = @WarehouseId AND o.Status = 1;
   ```

3. Fix: receive stock, cancel a stale allocated order, or change the warehouse.

### Stock figure looks wrong

Rebuild the figure from the audit trail and compare it with `StockLevel`:

```sql
SELECT s.ProductId, s.WarehouseId,
       s.QuantityOnHand,   SUM(m.QuantityChange) AS OnHandFromMovements,
       s.QuantityReserved, SUM(m.ReservedChange) AS ReservedFromMovements
FROM dbo.StockLevel s
LEFT JOIN dbo.StockMovement m ON m.ProductId = s.ProductId AND m.WarehouseId = s.WarehouseId
GROUP BY s.ProductId, s.WarehouseId, s.QuantityOnHand, s.QuantityReserved
HAVING s.QuantityOnHand   <> ISNULL(SUM(m.QuantityChange), 0)
    OR s.QuantityReserved <> ISNULL(SUM(m.ReservedChange), 0);
```

* **No rows:** the stock figures match the audit trail. The difference is real (a
  count, receipt or dispatch was posted). Look at the recent movements on the
  product page to see who posted what.
* **Rows returned:** something changed `StockLevel` without going through the
  procedures (a manual script, for example). Treat this as a P1: find the
  change and write an RCA.

### "This product was changed by another user"

Optimistic concurrency working as designed: two people edited the same product.
Reload, re-apply the change and save again. If it happens repeatedly to one
user, check whether a scheduled job is updating products.

### Desktop app is slow or freezes

1. Is it slow for everyone, or one user? One user over Remote Desktop usually
   means the network. Check whether the web app is also slow.
2. Find slow statements running right now:

   ```sql
   SELECT r.session_id, r.status, r.wait_type, r.wait_time, r.blocking_session_id,
          OBJECT_NAME(t.objectid, t.dbid) AS [procedure], r.total_elapsed_time
   FROM sys.dm_exec_requests r
   CROSS APPLY sys.dm_exec_sql_text(r.sql_handle) t
   WHERE r.session_id <> @@SPID;
   ```

   A non-zero `blocking_session_id` means a lock wait: find what the blocking
   session is doing before killing anything.
3. Slowest procedures since the last restart:

   ```sql
   SELECT TOP 10 OBJECT_NAME(object_id, database_id) AS [procedure],
          execution_count, total_elapsed_time / execution_count / 1000 AS avg_ms
   FROM sys.dm_exec_procedure_stats
   WHERE database_id = DB_ID()
   ORDER BY avg_ms DESC;
   ```

### "The database is busy or cannot be reached"

The app retried and gave up (see [ADR-0003](adr/0003-retry-policy.md)).
Check `/health`, SQL Server availability and the network. For a write (allocate,
dispatch, adjust), **check whether it went through** before the user tries again:
the order status and the product's recent movements show this.

### Web app returns 500 errors

Use the reference on the error page to find the stack trace in the web log. If
the error came from SQL Server, there is usually a matching row in `dbo.ErrorLog`
at the same time.

### Desktop app will not start

The start-up error names the problem and the log file. Common causes:

* `appsettings.json` is missing or invalid next to `StockFlow.Desktop.exe`.
* `Provider` is `SqlServer` but `ConnectionString` is empty, and
  `STOCKFLOW_DESKTOP_CONNECTION` is not set.
* The .NET 8 Desktop Runtime is not installed.
