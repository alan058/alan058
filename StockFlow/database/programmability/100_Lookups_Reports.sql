/*
    Error numbers raised by StockFlow procedures (mapped to .NET exceptions by SqlErrorTranslator):
        50001  Insufficient stock
        50002  Invalid order state for the requested action
        50003  Concurrency conflict (record changed by another user)
        50004  Record not found
        50005  Validation failed
*/

/* Returns three result sets: categories, active warehouses, active customers. */
CREATE OR ALTER PROCEDURE dbo.usp_Lookup_GetAll
AS
BEGIN
    SET NOCOUNT ON;

    SELECT CategoryId AS Id, CAST(NULL AS NVARCHAR(20)) AS Code, Name
    FROM dbo.Category
    ORDER BY Name;

    SELECT WarehouseId AS Id, Code, Name
    FROM dbo.Warehouse
    WHERE IsActive = 1
    ORDER BY Code;

    SELECT CustomerId AS Id, AccountCode AS Code, Name
    FROM dbo.Customer
    WHERE IsActive = 1
    ORDER BY Name;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_Dashboard_Summary
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @TodayStart DATETIME2(0) = CAST(CAST(SYSUTCDATETIME() AS DATE) AS DATETIME2(0));

    WITH ProductStock AS
    (
        SELECT p.ProductId,
               p.ReorderLevel,
               p.UnitPrice,
               ISNULL(SUM(s.QuantityOnHand), 0)                      AS OnHand,
               ISNULL(SUM(s.QuantityOnHand - s.QuantityReserved), 0) AS Available
        FROM dbo.Product p
        LEFT JOIN dbo.StockLevel s ON s.ProductId = p.ProductId
        WHERE p.IsActive = 1
        GROUP BY p.ProductId, p.ReorderLevel, p.UnitPrice
    )
    SELECT
        (SELECT COUNT(*) FROM ProductStock)                                     AS ActiveProducts,
        (SELECT COUNT(*) FROM ProductStock WHERE Available <= ReorderLevel)     AS LowStockProducts,
        (SELECT ISNULL(SUM(OnHand * UnitPrice), 0) FROM ProductStock)           AS StockValue,
        (SELECT COUNT(*) FROM dbo.SalesOrder WHERE Status = 0)                  AS OpenOrders,
        (SELECT COUNT(*) FROM dbo.SalesOrder WHERE Status = 1)                  AS AllocatedOrders,
        (SELECT COUNT(*) FROM dbo.SalesOrder WHERE Status = 2
                                             AND DispatchedAt >= @TodayStart)   AS DispatchedToday;
END
GO

/* Products whose available stock (across all warehouses) is at or below the reorder level. */
CREATE OR ALTER PROCEDURE dbo.usp_Report_LowStock
AS
BEGIN
    SET NOCOUNT ON;

    SELECT p.ProductId,
           p.Sku,
           p.Name,
           c.Name                                                AS CategoryName,
           p.ReorderLevel,
           ISNULL(SUM(s.QuantityOnHand), 0)                      AS QuantityOnHand,
           ISNULL(SUM(s.QuantityReserved), 0)                    AS QuantityReserved,
           ISNULL(SUM(s.QuantityOnHand - s.QuantityReserved), 0) AS QuantityAvailable,
           /* Suggest topping up to twice the reorder level. */
           (p.ReorderLevel * 2) - ISNULL(SUM(s.QuantityOnHand - s.QuantityReserved), 0) AS SuggestedOrderQuantity
    FROM dbo.Product p
    JOIN dbo.Category c        ON c.CategoryId = p.CategoryId
    LEFT JOIN dbo.StockLevel s ON s.ProductId  = p.ProductId
    WHERE p.IsActive = 1
    GROUP BY p.ProductId, p.Sku, p.Name, c.Name, p.ReorderLevel
    HAVING ISNULL(SUM(s.QuantityOnHand - s.QuantityReserved), 0) <= p.ReorderLevel
    ORDER BY ISNULL(SUM(s.QuantityOnHand - s.QuantityReserved), 0) - p.ReorderLevel, p.Sku;
END
GO

/* Lightweight connectivity check used by the health endpoint. */
CREATE OR ALTER PROCEDURE dbo.usp_Health_Ping
AS
BEGIN
    SET NOCOUNT ON;
    SELECT CAST(1 AS BIT) AS Ok;
END
GO
