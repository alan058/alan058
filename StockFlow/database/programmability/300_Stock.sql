/*
    Receives (@MovementType = 1) or adjusts (@MovementType = 2) stock for one
    product in one warehouse, and writes the audit row in the same transaction.

    The stock row is read with UPDLOCK so two users adjusting the same item at
    the same time are serialised instead of both reading the old quantity.
    Returns the resulting stock level.
*/
CREATE OR ALTER PROCEDURE dbo.usp_Stock_Adjust
    @ProductId      INT,
    @WarehouseId    INT,
    @MovementType   TINYINT,
    @QuantityChange INT,
    @Reference      NVARCHAR(50)  = NULL,
    @Reason         NVARCHAR(200) = NULL,
    @User           NVARCHAR(100)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        IF @MovementType NOT IN (1, 2)
        BEGIN;
            THROW 50005, N'Only receipts and adjustments can be posted directly.', 1;
        END
        IF @QuantityChange = 0 OR (@MovementType = 1 AND @QuantityChange < 0)
        BEGIN;
            THROW 50005, N'Quantity must be positive for a receipt and non-zero for an adjustment.', 1;
        END
        IF @MovementType = 2 AND NULLIF(LTRIM(RTRIM(@Reason)), N'') IS NULL
        BEGIN;
            THROW 50005, N'A reason is required for a stock adjustment.', 1;
        END

        BEGIN TRANSACTION;

        IF NOT EXISTS (SELECT 1 FROM dbo.Product WHERE ProductId = @ProductId)
        BEGIN;
            THROW 50004, N'Product not found.', 1;
        END
        IF NOT EXISTS (SELECT 1 FROM dbo.Warehouse WHERE WarehouseId = @WarehouseId AND IsActive = 1)
        BEGIN;
            THROW 50004, N'Warehouse not found or inactive.', 1;
        END

        DECLARE @OnHand INT, @Reserved INT;

        SELECT @OnHand = QuantityOnHand, @Reserved = QuantityReserved
        FROM dbo.StockLevel WITH (UPDLOCK, HOLDLOCK)
        WHERE ProductId = @ProductId AND WarehouseId = @WarehouseId;

        IF @OnHand IS NULL
        BEGIN
            INSERT dbo.StockLevel (ProductId, WarehouseId) VALUES (@ProductId, @WarehouseId);
            SELECT @OnHand = 0, @Reserved = 0;
        END

        IF @OnHand + @QuantityChange < @Reserved
        BEGIN
            DECLARE @Msg NVARCHAR(400) = CONCAT(
                N'Adjustment would leave ', @OnHand + @QuantityChange, N' on hand but ',
                @Reserved, N' are reserved for orders. Available to adjust: ', @OnHand - @Reserved, N'.');
            THROW 50001, @Msg, 1;
        END

        UPDATE dbo.StockLevel
        SET QuantityOnHand = QuantityOnHand + @QuantityChange
        WHERE ProductId = @ProductId AND WarehouseId = @WarehouseId;

        INSERT dbo.StockMovement (ProductId, WarehouseId, MovementType, QuantityChange, ReservedChange, Reference, Reason, CreatedBy)
        VALUES (@ProductId, @WarehouseId, @MovementType, @QuantityChange, 0, @Reference, @Reason, @User);

        COMMIT TRANSACTION;

        SELECT s.ProductId, s.WarehouseId, w.Code AS WarehouseCode, w.Name AS WarehouseName,
               s.QuantityOnHand, s.QuantityReserved
        FROM dbo.StockLevel s
        JOIN dbo.Warehouse w ON w.WarehouseId = s.WarehouseId
        WHERE s.ProductId = @ProductId AND s.WarehouseId = @WarehouseId;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        EXEC dbo.usp_ErrorLog_Write;
        THROW;
    END CATCH
END
GO
