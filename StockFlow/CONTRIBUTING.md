# Contributing

## Before you change anything

1. **Understand the current behaviour.** Find the stored procedure, the service
   and the screens involved. `ARCHITECTURE.md` shows how a request flows.
2. **Assess the impact.** Search for every caller of what you are changing.
   Remember that both the web app **and** the desktop app use `StockFlow.Core`.
3. **For a defect, find the root cause.** Write a failing test that reproduces
   it *before* fixing it. The test stays as the regression test.

## Coding standards

### C#
* Nullable reference types are on and warnings are errors.
* Business failures throw a `StockFlowException` subclass with a message a user
  can act on. Do not catch `Exception` except at the top of a request or event
  handler.
* Async all the way down. No `.Result` or `.Wait()` in application code.

### VB.NET
* `Option Strict On`, `Option Explicit On` (set for every project in
  `Directory.Build.props`). No late binding.
* Event handlers that do I/O are `Async Sub` and wrap their work in
  `Try … Catch ex As Exception … ErrorHandler.Handle(...)`.
* Never block the UI thread with database calls.
* Remember that VB is case-insensitive: a local `csv` hides the `Csv` class.

### SQL
* Every procedure starts with `SET NOCOUNT ON; SET XACT_ABORT ON;` when it writes.
* Writes use `BEGIN TRY / BEGIN TRANSACTION … COMMIT / END TRY` and a `CATCH`
  that rolls back, calls `dbo.usp_ErrorLog_Write`, then `THROW`s.
* Business rule failures: `THROW 50001`–`50005` (see `100_Lookups_Reports.sql`
  for the list). Add a new number to `SqlErrorTranslator` if you need one.
* Lock rows you read in order to decide what to write (`UPDLOCK, HOLDLOCK`).
* Name constraints and indexes explicitly (`PK_`, `FK_`, `UQ_`, `CK_`, `IX_`).

## Code review checklist

Reviewers check, and authors check first:

- [ ] Does the change do what the ticket asked, and nothing else?
- [ ] For a defect: is the root cause explained and fixed, and is there a regression test?
- [ ] Are both front ends still correct if a shared service changed?
- [ ] Are errors handled once, in the right layer, with a useful message?
- [ ] Database: a new migration (not an edited one)? Indexes for new joins and filters? Constraints for new rules?
- [ ] Will the old desktop version still work against the new database during roll-out?
- [ ] Are names clear, and are comments about *why* rather than *what*?
- [ ] `CHANGELOG.md` updated?

## Running everything locally

See the [README](README.md#quick-start). Set `STOCKFLOW_TEST_SQL` so the SQL
Server contract tests run, as they do in CI.
