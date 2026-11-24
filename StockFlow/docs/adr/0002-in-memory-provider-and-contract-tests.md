# ADR-0002: An in-memory data provider, kept honest by contract tests

* **Status:** Accepted

## Context

Developers, testers and reviewers need to run the applications without
installing SQL Server. Service-level tests should run in milliseconds. But a
fake data layer that behaves differently from the real one is worse than none:
tests pass, and production fails.

## Decision

* `StockFlow.Data.InMemory.InMemoryStockFlowStore` implements every repository
  interface with the same rules and the same error messages as the stored
  procedures. It is thread-safe and returns copies, never its own objects.
* `RepositoryContractTests` is an abstract xUnit class. It is subclassed twice:
  `InMemoryContractTests` and `SqlServerContractTests`. Every behaviour is
  written once and checked against both implementations.
* The SQL Server subclass deploys a fresh database from `database/` for each
  run and drops it afterwards, so tests never depend on a shared database.
* CI runs SQL Server in a container and **fails if any test was skipped**.

## Consequences

* A behaviour change in a stored procedure without the same change in the
  in-memory store (or the other way round) fails CI.
* The demo provider is safe for training and demos. It is never a production option.
* Some SQL-only behaviour (lock waits, plan choice) cannot be reproduced in
  memory. The concurrency contract test still runs against both, and is most
  meaningful against SQL Server.
