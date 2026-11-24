# Architecture

## Goals

1. **Correct stock figures under concurrent use.** Several people allocate,
   dispatch and adjust at the same time from two different front ends.
2. **One set of business rules.** The VB.NET client and the web portal must
   behave the same.
3. **Easy to support.** When something goes wrong, the user gets a clear message
   and support gets a log entry they can find.
4. **Safe to change.** A developer can change a stored procedure or a service
   and know within minutes whether something else broke.

## Layers

| Layer | Project | Responsibility | Depends on |
|---|---|---|---|
| Presentation (desktop) | `StockFlow.Desktop` (VB.NET) | WinForms screens, user feedback, local file log | Core, Data |
| Presentation (web) | `StockFlow.Web` (C#) | MVC controllers, Razor views, HTTP concerns | Core, Data |
| Business | `StockFlow.Core` (C#) | Models, services, validation, order state rules, exception types, repository **interfaces** | nothing |
| Data access | `StockFlow.Data` (C#) | Repository **implementations** (SQL Server and in-memory), database deployer | Core, Microsoft.Data.SqlClient |
| Database | `database/*.sql` | Tables, constraints, stored procedures, audit trail | — |

`StockFlow.Core` has no dependencies, which keeps the business rules easy to
test and lets both front ends use them unchanged.

## Request flow: allocating an order

```
User clicks "Allocate"
  → OrdersController.Allocate (web)  or  OrdersForm.Allocate_Click (desktop)
    → OrderService.AllocateAsync
        1. loads the order; if it is not Open, throws InvalidOrderStateException
           (a clear message, without a database transaction)
    → IOrderRepository.AllocateAsync
        → SqlOrderRepository → SqlExecutor → EXEC dbo.usp_Order_Allocate
            BEGIN TRAN
              lock the order row (UPDLOCK, HOLDLOCK) and re-check its status
              lock the stock rows for every line
              if any line is short → THROW 50001 with the list of short lines
              reserve stock, write StockMovement rows, set status = Allocated
            COMMIT
        ← SqlException 50001 → SqlErrorTranslator → InsufficientStockException
  ← web: TempData error message on the order page
  ← desktop: ErrorHandler shows the message as a warning and logs it as INFO
```

The state check runs twice on purpose. The service check gives a fast, clear
message in the normal case. The check inside the transaction is the one that
guarantees correctness when two users act at the same moment.

## Error handling strategy

| Kind | Type | User sees | Logged as |
|---|---|---|---|
| Business rule (short stock, wrong state, invalid input) | `StockFlowException` subclasses | The specific message | Info |
| Concurrency (someone else saved first) | `ConcurrencyException` | "Reload and try again" | Info |
| Database busy or unreachable | `DataAccessException` with `IsTransient = true` | "Try again in a moment" + reference | Warning / Error |
| Anything else (a defect) | any other exception | Generic message + **reference id** | Error with stack trace |

* Stored procedures raise business errors with `THROW 5000x` and log
  *unexpected* errors to `dbo.ErrorLog` from their `CATCH` blocks.
* `SqlErrorTranslator` maps SQL error numbers to the exception types above, so
  no code outside `StockFlow.Data` knows about SQL error numbers.
* The web app adds a correlation id to every request (`X-Correlation-ID`) and
  shows it on the error page. The desktop app shows an 8-character incident id
  that is also written to its log file.

## Data access

* **Stored procedures only.** No SQL text is built in application code. This
  rules out SQL injection, lets a DBA tune queries without a release of the
  apps, and gives one obvious place for each query. See
  [ADR-0001](adr/0001-stored-procedures-for-data-access.md).
* **ADO.NET, no ORM.** The procedures return exactly the shape the screens
  need, so mapping is simple. Columns are read by name.
* **Retries only where they are safe.** See [ADR-0003](adr/0003-retry-policy.md).
* **Two repository implementations.** The in-memory one lets anyone run the
  apps without SQL Server and makes the service tests fast. A shared contract
  test suite keeps it behaving exactly like the stored procedures. See
  [ADR-0002](adr/0002-in-memory-provider-and-contract-tests.md).

## Database design notes

* `StockLevel` has one row per product per warehouse with `QuantityOnHand` and
  `QuantityReserved`. CHECK constraints make `0 <= Reserved <= OnHand`
  impossible to break, whatever code path writes to the table.
* `StockMovement` is append-only. On-hand stock for any product can be rebuilt
  by summing its movements, which makes stock discrepancies traceable.
* `SalesOrder.OrderNumber` is a persisted computed column (`SO-000123`), so it
  cannot drift from the id.
* `Product.RowVer` (`rowversion`) gives optimistic concurrency on edits.
* `dbo.SchemaVersion` records which migrations have run, with a checksum, so an
  edited migration is detected instead of silently ignored.

## Performance considerations

| Area | What was done |
|---|---|
| Search procedures with optional filters | `OPTION (RECOMPILE)`, so each call gets a plan for its actual filters (avoids parameter-sniffing problems) |
| Creating an order | All lines are sent in one table-valued parameter: one round trip and one transaction |
| Indexes | On every foreign key used in joins, plus `(Status, OrderDate)` for the order list and `(ProductId, CreatedAt)` for movements |
| Dashboard | Independent queries run in parallel |
| WinForms search | Waits until the user pauses typing (350 ms), and results from older searches are discarded (SF-087) |
| WinForms grids | Double buffering turned on to stop flicker and slow repaints over Remote Desktop |
