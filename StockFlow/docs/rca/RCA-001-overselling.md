# RCA-001: Stock allocated to more orders than were in the warehouse

> This is a worked example written for this project. It shows the format used
> for root-cause analyses and documents why `usp_Order_Allocate` locks the
> way it does.

| | |
|---|---|
| **Severity** | High: customers were promised stock that did not exist |
| **Symptom** | Two orders for the last units of a product were both *Allocated*. One could not be picked. |
| **Detected by** | Warehouse picker; later reproduced by an automated test |
| **Fixed in** | 1.0.0 (`usp_Order_Allocate`, `CK_StockLevel_ReservedOnHand`) |
| **Regression test** | `RepositoryContractTests.ConcurrentAllocations_NeverOversell` |

## Timeline

1. Two sales staff allocate different orders for the same product within the same second.
2. Both allocations succeed. Reserved stock is now higher than on-hand stock.
3. At picking, one order cannot be filled. The customer is let down.

## Investigation

The first version of the allocation logic did this:

```sql
-- 1. check
IF EXISTS (SELECT ... WHERE QuantityOnHand - QuantityReserved < l.Quantity)
    THROW 50001, ...;
-- 2. reserve
UPDATE StockLevel SET QuantityReserved += l.Quantity ...
```

Under the default `READ COMMITTED` isolation level, the shared locks taken by
step 1 are released as soon as each row is read. Two sessions can both pass the
check before either one reaches step 2. This is a classic *check-then-act* race.

The race is reproduced by starting five allocations in parallel for stock
that covers only two of them, which is exactly what the regression test does.

### Why the obvious quick fixes were rejected

| Quick fix | Why not |
|---|---|
| Check stock again in the VB / web code just before allocating | The same race, only moved further from the data |
| A "please refresh" warning on the screen | Hides the symptom; data is still corrupted |
| `SERIALIZABLE` isolation for the whole procedure | Works, but its range locks can block unrelated products and raise the risk of deadlocks under load |
| A nightly job to find over-reserved stock | Detects damage after the customer has already been promised the stock |

## Root cause

The availability check and the reservation were not atomic, and nothing in
the database stopped reserved stock from exceeding on-hand stock.

## Fix

1. **Lock what you check.** `usp_Order_Allocate` reads the order row and every
   stock row it will change with `UPDLOCK, HOLDLOCK` inside the transaction,
   *before* checking availability. A second allocation for the same stock waits
   for the first to commit, then sees the updated reserved quantity.
   Only the rows involved are locked, so unrelated products are not blocked.
2. **Make the bad state impossible.** `CK_StockLevel_ReservedOnHand`
   (`QuantityReserved <= QuantityOnHand`) means that even a future bug or a
   manual script cannot oversell. The statement fails instead.
3. **All or nothing.** The check covers every line before anything is reserved,
   and `XACT_ABORT ON` guarantees rollback on any error, so a partly allocated
   order cannot exist.
4. **Clear feedback.** The error lists each short line (`AUD-002 (requested 30,
   available 18)`), so the user knows what to do next.

## Prevention

* `ConcurrentAllocations_NeverOversell` runs on every CI build against real SQL
  Server and against the in-memory store.
* The PR template asks for the root cause and a regression test for every defect fix.
* [ARCHITECTURE.md](../ARCHITECTURE.md) documents the "check twice, lock once" pattern.
