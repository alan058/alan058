# ADR-0001: Stored procedures for all data access

* **Status:** Accepted

## Context

Two applications (a VB.NET desktop client and an ASP.NET MVC web portal)
change the same stock and order data. The rules that protect that data
(no overselling, no negative stock, an audit row for every change) must hold
no matter which application, or which future script, makes the change.

The team is comfortable with T-SQL. The existing business systems already use
stored procedures, and the DBA wants to be able to see and tune every query.

## Decision

* All reads and writes go through stored procedures in `database/programmability`.
* Application code never builds SQL text. Parameters are always typed
  (`SqlDbType` with a length), which also keeps plan-cache reuse predictable.
* Multi-step changes (allocate, dispatch, cancel, adjust) are each **one
  procedure with one transaction**, so they are all-or-nothing.
* Procedures raise expected business failures with `THROW 50001`–`50005`.
  Unexpected errors are written to `dbo.ErrorLog` and re-thrown.
* Data integrity is also enforced declaratively with foreign keys, unique keys
  and CHECK constraints, as a last line of defence.

## Consequences

* Positive: no SQL injection surface; queries can be tuned and redeployed
  without rebuilding either application; transactional rules live next to the
  data they protect.
* Positive: the contract tests exercise the procedures directly, giving them
  real regression coverage.
* Negative: business rules exist in two places (service validation and
  procedure checks). This is deliberate: the service gives early, friendly
  messages and the procedure guarantees correctness. The contract tests catch
  any drift between the two.
* Negative: procedures must be deployed together with the applications. The
  deployer re-applies all procedures on every release, so the database always
  matches source control.
