/*
    Sales order lifecycle:  Open (0) -> Allocated (1) -> Dispatched (2)
                            Open / Allocated -> Cancelled (3)

    Allocation reserves stock; dispatch removes it from on-hand; cancelling an
    allocated order releases the reservation. Every step writes StockMovement rows.
*/

CREATE OR ALTER PROCEDURE dbo.usp_Order_Search
    @Status     TINYINT       = NULL,
    @CustomerId INT           = NULL,
    @SearchTerm NVARCHAR(100) = NULL,
    @Top        INT           = 200
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Pattern NVARCHAR(210) = NULL;
    IF NULLIF(LTRIM(RTRIM(@SearchTerm)), N'') IS NOT NULL
        SET @Pattern = N'%' + REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(@SearchTerm)), N'[', N'[[]'), N'%', N'[%]'), N'_', N'[_]') + N'%';

    SELECT TOP (@Top)
           o.SalesOrderId, o.OrderNumber, o.CustomerId, c.Name AS CustomerName,
           o.WarehouseId, w.Code AS WarehouseCode, o.Status, o.OrderDate, o.RequiredDate,
           ISNULL(t.LineCount, 0)     AS LineCount,
           ISNULL(t.TotalQuantity, 0) AS TotalQuantity,
           ISNULL(t.TotalValue, 0)    AS TotalValue
    FROM dbo.SalesOrder o
    JOIN dbo.Customer  c ON c.CustomerId  = o.CustomerId
    JOIN dbo.Warehouse w ON w.WarehouseId = o.WarehouseId
    OUTER APPLY
    (
        SELECT COUNT(*)                    AS LineCount,
               SUM(l.Quantity)             AS TotalQuantity,
               SUM(l.Quantity * l.UnitPrice) AS TotalValue
        FROM dbo.SalesOrderLine l
        WHERE l.SalesOrderId = o.SalesOrderId
    ) t
    WHERE (@Status IS NULL OR o.Status = @Status)
      AND (@CustomerId IS NULL OR o.CustomerId = @CustomerId)
      AND (@Pattern IS NULL OR o.OrderNumber LIKE @Pattern OR c.Name LIKE @Pattern OR c.AccountCode LIKE @Pattern)
    ORDER BY o.OrderDate DESC, o.SalesOrderId DESC
    OPTION (RECOMPILE);
END
GO

/* Result set 1: order header. Result set 2: lines with stock available in the order's warehouse. */
CREATE OR ALTER PROCEDURE dbo.usp_Order_GetById
    @SalesOrderId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT o.SalesOrderId, o.OrderNumber, o.CustomerId, c.Name AS CustomerName,
           o.WarehouseId, w.Code AS WarehouseCode, o.Status, o.OrderDate, o.RequiredDate,
           o.Notes, o.CreatedBy, o.DispatchedAt
    FROM dbo.SalesOrder o
    JOIN dbo.Customer  c ON c.CustomerId  = o.CustomerId
    JOIN dbo.Warehouse w ON w.WarehouseId = o.WarehouseId
    WHERE o.SalesOrderId = @SalesOrderId;

    SELECT l.SalesOrderLineId, l.ProductId, p.Sku, p.Name AS ProductName, l.Quantity, l.UnitPrice,
           ISNULL(s.QuantityOnHand - s.QuantityReserved, 0) AS QuantityAvailable
    FROM dbo.SalesOrderLine l
    JOIN dbo.SalesOrder o      ON o.SalesOrderId = l.SalesOrderId
    JOIN dbo.Product p         ON p.ProductId    = l.ProductId
    LEFT JOIN dbo.StockLevel s ON s.ProductId    = l.ProductId AND s.WarehouseId = o.WarehouseId
    WHERE l.SalesOrderId = @SalesOrderId
    ORDER BY p.Sku;
END
GO

/* Creates an Open order with all of its lines in one transaction. Prices are taken from the product. */
CREATE OR ALTER PROCEDURE dbo.usp_Order_Create
    @CustomerId   INT,
    @WarehouseId  INT,
    @RequiredDate DATE          = NULL,
    @Notes        NVARCHAR(500) = NULL,
    @User         NVARCHAR(100),
    @Lines        dbo.OrderLineList READONLY
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        IF NOT EXISTS (SELECT 1 FROM @Lines)
        BEGIN;
            THROW 50005, N'An order needs at least one line.', 1;
        END
        IF EXISTS (SELECT 1 FROM @Lines WHERE Quantity <= 0)
        BEGIN;
            THROW 50005, N'Every line quantity must be greater than zero.', 1;
        END
        IF NOT EXISTS (SELECT 1 FROM dbo.Customer WHERE CustomerId = @CustomerId AND IsActive = 1)
        BEGIN;
            THROW 50004, N'Customer not found or inactive.', 1;
        END
        IF NOT EXISTS (SELECT 1 FROM dbo.Warehouse WHERE WarehouseId = @WarehouseId AND IsActive = 1)
        BEGIN;
            THROW 50004, N'Warehouse not found or inactive.', 1;
        END
        IF EXISTS (SELECT 1 FROM @Lines l
                   LEFT JOIN dbo.Product p ON p.ProductId = l.ProductId AND p.IsActive = 1
                   WHERE p.ProductId IS NULL)
        BEGIN;
            THROW 50005, N'One or more products do not exist or are inactive.', 1;
        END

        BEGIN TRANSACTION;

        INSERT dbo.SalesOrder (CustomerId, WarehouseId, RequiredDate, Notes, CreatedBy)
        VALUES (@CustomerId, @WarehouseId, @RequiredDate, @Notes, @User);

        DECLARE @SalesOrderId INT = CAST(SCOPE_IDENTITY() AS INT);

        INSERT dbo.SalesOrderLine (SalesOrderId, ProductId, Quantity, UnitPrice)
        SELECT @SalesOrderId, l.ProductId, l.Quantity, p.UnitPrice
        FROM @Lines l
        JOIN dbo.Product p ON p.ProductId = l.ProductId;

        COMMIT TRANSACTION;

        SELECT @SalesOrderId AS SalesOrderId;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        EXEC dbo.usp_ErrorLog_Write;
        THROW;
    END CATCH
END
GO

/*
    Reserves stock for every line of an Open order, all or nothing.

    The stock rows are locked (UPDLOCK, HOLDLOCK) before availability is
    checked, so two orders competing for the last units cannot both pass the
    check. See docs/rca/RCA-001-overselling.md.
*/
CREATE OR ALTER PROCEDURE dbo.usp_Order_Allocate
    @SalesOrderId INT,
    @User         NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRANSACTION;

        DECLARE @Status TINYINT, @WarehouseId INT, @OrderNumber NVARCHAR(20);

        SELECT @Status = Status, @WarehouseId = WarehouseId, @OrderNumber = OrderNumber
        FROM dbo.SalesOrder WITH (UPDLOCK, HOLDLOCK)
        WHERE SalesOrderId = @SalesOrderId;

        IF @Status IS NULL
        BEGIN;
            THROW 50004, N'Sales order not found.', 1;
        END
        IF @Status <> 0
        BEGIN;
            THROW 50002, N'Only open orders can be allocated.', 1;
        END

        /* Make sure a stock row exists for every line so it can be locked. */
        INSERT dbo.StockLevel (ProductId, WarehouseId)
        SELECT l.ProductId, @WarehouseId
        FROM dbo.SalesOrderLine l
        WHERE l.SalesOrderId = @SalesOrderId
          AND NOT EXISTS (SELECT 1 FROM dbo.StockLevel s WITH (UPDLOCK, HOLDLOCK)
                          WHERE s.ProductId = l.ProductId AND s.WarehouseId = @WarehouseId);

        DECLARE @Shortages TABLE (Sku NVARCHAR(32) NOT NULL, Requested INT NOT NULL, Available INT NOT NULL);

        INSERT @Shortages (Sku, Requested, Available)
        SELECT p.Sku, l.Quantity, s.QuantityOnHand - s.QuantityReserved
        FROM dbo.SalesOrderLine l
        JOIN dbo.Product p                        ON p.ProductId = l.ProductId
        JOIN dbo.StockLevel s WITH (UPDLOCK, HOLDLOCK)
                                                  ON s.ProductId = l.ProductId AND s.WarehouseId = @WarehouseId
        WHERE l.SalesOrderId = @SalesOrderId
          AND s.QuantityOnHand - s.QuantityReserved < l.Quantity;

        IF EXISTS (SELECT 1 FROM @Shortages)
        BEGIN
            DECLARE @Msg NVARCHAR(2000) =
                N'Insufficient stock to allocate ' + @OrderNumber + N': ' +
                (SELECT STRING_AGG(CONCAT(Sku, N' (requested ', Requested, N', available ', Available, N')'), N'; ')
                 FROM @Shortages);
            THROW 50001, @Msg, 1;
        END

        UPDATE s
        SET s.QuantityReserved = s.QuantityReserved + l.Quantity
        FROM dbo.StockLevel s
        JOIN dbo.SalesOrderLine l ON l.ProductId = s.ProductId
        WHERE l.SalesOrderId = @SalesOrderId
          AND s.WarehouseId = @WarehouseId;

        INSERT dbo.StockMovement (ProductId, WarehouseId, MovementType, QuantityChange, ReservedChange, Reference, CreatedBy)
        SELECT l.ProductId, @WarehouseId, 3, 0, l.Quantity, @OrderNumber, @User
        FROM dbo.SalesOrderLine l
        WHERE l.SalesOrderId = @SalesOrderId;

        UPDATE dbo.SalesOrder
        SET Status = 1, ModifiedAt = SYSUTCDATETIME()
        WHERE SalesOrderId = @SalesOrderId;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        EXEC dbo.usp_ErrorLog_Write;
        THROW;
    END CATCH
END
GO

/* Ships an Allocated order: reserved stock leaves the warehouse. */
CREATE OR ALTER PROCEDURE dbo.usp_Order_Dispatch
    @SalesOrderId INT,
    @User         NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRANSACTION;

        DECLARE @Status TINYINT, @WarehouseId INT, @OrderNumber NVARCHAR(20);

        SELECT @Status = Status, @WarehouseId = WarehouseId, @OrderNumber = OrderNumber
        FROM dbo.SalesOrder WITH (UPDLOCK, HOLDLOCK)
        WHERE SalesOrderId = @SalesOrderId;

        IF @Status IS NULL
        BEGIN;
            THROW 50004, N'Sales order not found.', 1;
        END
        IF @Status <> 1
        BEGIN;
            THROW 50002, N'Only allocated orders can be dispatched.', 1;
        END

        UPDATE s
        SET s.QuantityOnHand   = s.QuantityOnHand   - l.Quantity,
            s.QuantityReserved = s.QuantityReserved - l.Quantity
        FROM dbo.StockLevel s
        JOIN dbo.SalesOrderLine l ON l.ProductId = s.ProductId
        WHERE l.SalesOrderId = @SalesOrderId
          AND s.WarehouseId = @WarehouseId;

        INSERT dbo.StockMovement (ProductId, WarehouseId, MovementType, QuantityChange, ReservedChange, Reference, CreatedBy)
        SELECT l.ProductId, @WarehouseId, 5, -l.Quantity, -l.Quantity, @OrderNumber, @User
        FROM dbo.SalesOrderLine l
        WHERE l.SalesOrderId = @SalesOrderId;

        UPDATE dbo.SalesOrder
        SET Status = 2, DispatchedAt = SYSUTCDATETIME(), ModifiedAt = SYSUTCDATETIME()
        WHERE SalesOrderId = @SalesOrderId;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        EXEC dbo.usp_ErrorLog_Write;
        THROW;
    END CATCH
END
GO

/* Cancels an Open or Allocated order, releasing any reserved stock. */
CREATE OR ALTER PROCEDURE dbo.usp_Order_Cancel
    @SalesOrderId INT,
    @User         NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRANSACTION;

        DECLARE @Status TINYINT, @WarehouseId INT, @OrderNumber NVARCHAR(20);

        SELECT @Status = Status, @WarehouseId = WarehouseId, @OrderNumber = OrderNumber
        FROM dbo.SalesOrder WITH (UPDLOCK, HOLDLOCK)
        WHERE SalesOrderId = @SalesOrderId;

        IF @Status IS NULL
        BEGIN;
            THROW 50004, N'Sales order not found.', 1;
        END
        IF @Status NOT IN (0, 1)
        BEGIN;
            THROW 50002, N'Only open or allocated orders can be cancelled.', 1;
        END

        IF @Status = 1
        BEGIN
            UPDATE s
            SET s.QuantityReserved = s.QuantityReserved - l.Quantity
            FROM dbo.StockLevel s
            JOIN dbo.SalesOrderLine l ON l.ProductId = s.ProductId
            WHERE l.SalesOrderId = @SalesOrderId
              AND s.WarehouseId = @WarehouseId;

            INSERT dbo.StockMovement (ProductId, WarehouseId, MovementType, QuantityChange, ReservedChange, Reference, CreatedBy)
            SELECT l.ProductId, @WarehouseId, 4, 0, -l.Quantity, @OrderNumber, @User
            FROM dbo.SalesOrderLine l
            WHERE l.SalesOrderId = @SalesOrderId;
        END

        UPDATE dbo.SalesOrder
        SET Status = 3, ModifiedAt = SYSUTCDATETIME()
        WHERE SalesOrderId = @SalesOrderId;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        EXEC dbo.usp_ErrorLog_Write;
        THROW;
    END CATCH
END
GO
