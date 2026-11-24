# Changelog

All notable changes are listed here. Versions follow [Semantic Versioning](https://semver.org/).

> The defect numbers below (SF-xxx, RCA-001) are worked examples for this
> project. Each shows a root-cause fix with a regression test.

## [1.0.0]

### Added

- SQL Server database: products, warehouses, stock levels with reservations,
  customers, sales orders and a full stock-movement audit trail.
- Stored procedures for search, product maintenance, stock receipts and
  adjustments, and the order lifecycle (create, allocate, dispatch, cancel).
- `stockflow-dbdeploy`: tracked, checksummed migrations and repeatable
  procedure deployment.
- ASP.NET Core MVC web portal with Razor views: dashboard, products, stock
  receipts and adjustments, sales orders, low-stock report with CSV export,
  `/health` endpoint and correlation ids.
- VB.NET Windows Forms back-office client: product and stock overview, receive
  and adjust stock, order desk (allocate, dispatch, cancel), low-stock report.
- In-memory data provider for demos and fast tests.
- 85 automated tests, including a repository contract suite run against both
  the in-memory store and real SQL Server.

### Fixed

- **RCA-001** Two concurrent allocations could reserve the same stock.
  Allocation now locks the stock rows before checking availability, and a CHECK
  constraint makes over-reservation impossible.
  Test: `ConcurrentAllocations_NeverOversell`.
- **SF-112** Adding the same product twice to an order failed with a
  unique-key error. Lines for the same product are now merged into one.
  Test: `ConsolidateLines_MergesTheSameProduct`.
- **SF-087** Desktop product search sometimes showed results for an earlier
  search term. Searches now wait for the user to stop typing, and results from
  superseded searches are discarded.
- Web order form: blank quantity boxes on unused rows stopped the whole order
  from saving. Test: `CreateOrder_ThenAllocate_EndToEnd`.
- Product and order search treated `%`, `_` and `[` as wildcards.
  Test: `ProductSearch_TreatsWildcardCharactersLiterally`.
