/*
    Product search with optional filters.

    This is a "catch-all" query (optional parameters). OPTION (RECOMPILE) gives
    each call a plan built for its actual parameters, which avoids the classic
    parameter-sniffing problem where the first caller's plan (e.g. no filter)
    is reused for a very selective search, or the other way round.
*/
CREATE OR ALTER PROCEDURE dbo.usp_Product_Search
    @SearchTerm   NVARCHAR(100) = NULL,
    @CategoryId   INT           = NULL,
    @LowStockOnly BIT           = 0,
    @IncludeInactive BIT        = 0
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Pattern NVARCHAR(210) = NULL;
    IF NULLIF(LTRIM(RTRIM(@SearchTerm)), N'') IS NOT NULL
        /* Escape LIKE wildcards so "50%" or "A_1" are searched for literally. */
        SET @Pattern = N'%' + REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(@SearchTerm)), N'[', N'[[]'), N'%', N'[%]'), N'_', N'[_]') + N'%';

    SELECT p.ProductId,
           p.Sku,
           p.Name,
           c.Name                              AS CategoryName,
           p.UnitPrice,
           p.ReorderLevel,
           p.IsActive,
           ISNULL(SUM(s.QuantityOnHand), 0)    AS QuantityOnHand,
           ISNULL(SUM(s.QuantityReserved), 0)  AS QuantityReserved
    FROM dbo.Product p
    JOIN dbo.Category c        ON c.CategoryId = p.CategoryId
    LEFT JOIN dbo.StockLevel s ON s.ProductId  = p.ProductId
    WHERE (@Pattern IS NULL OR p.Sku LIKE @Pattern OR p.Name LIKE @Pattern)
      AND (@CategoryId IS NULL OR p.CategoryId = @CategoryId)
      AND (@IncludeInactive = 1 OR p.IsActive = 1)
    GROUP BY p.ProductId, p.Sku, p.Name, c.Name, p.UnitPrice, p.ReorderLevel, p.IsActive
    HAVING (@LowStockOnly = 0
            OR ISNULL(SUM(s.QuantityOnHand - s.QuantityReserved), 0) <= p.ReorderLevel)
    ORDER BY p.Sku
    OPTION (RECOMPILE);
END
GO

/* Result set 1: the product. Result set 2: stock in every active warehouse (zero rows included). */
CREATE OR ALTER PROCEDURE dbo.usp_Product_GetById
    @ProductId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT p.ProductId, p.Sku, p.Name, p.CategoryId, c.Name AS CategoryName,
           p.UnitPrice, p.ReorderLevel, p.IsActive, p.RowVer
    FROM dbo.Product p
    JOIN dbo.Category c ON c.CategoryId = p.CategoryId
    WHERE p.ProductId = @ProductId;

    SELECT @ProductId AS ProductId,
           w.WarehouseId,
           w.Code                            AS WarehouseCode,
           w.Name                            AS WarehouseName,
           ISNULL(s.QuantityOnHand, 0)       AS QuantityOnHand,
           ISNULL(s.QuantityReserved, 0)     AS QuantityReserved
    FROM dbo.Warehouse w
    LEFT JOIN dbo.StockLevel s ON s.WarehouseId = w.WarehouseId AND s.ProductId = @ProductId
    WHERE w.IsActive = 1
      AND EXISTS (SELECT 1 FROM dbo.Product WHERE ProductId = @ProductId)
    ORDER BY w.Code;
END
GO

/*
    Inserts (@ProductId NULL) or updates a product. Returns the ProductId.
    Updates use optimistic concurrency: @RowVersion must match the row the
    user loaded, otherwise 50003 is raised and nothing is overwritten.
*/
CREATE OR ALTER PROCEDURE dbo.usp_Product_Save
    @ProductId    INT            = NULL,
    @Sku          NVARCHAR(32),
    @Name         NVARCHAR(200),
    @CategoryId   INT,
    @UnitPrice    DECIMAL(18,2),
    @ReorderLevel INT,
    @IsActive     BIT,
    @RowVersion   BINARY(8)      = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRANSACTION;

        IF NOT EXISTS (SELECT 1 FROM dbo.Category WHERE CategoryId = @CategoryId)
        BEGIN;
            THROW 50005, N'Category does not exist.', 1;
        END

        IF EXISTS (SELECT 1 FROM dbo.Product WITH (UPDLOCK, HOLDLOCK)
                   WHERE Sku = @Sku AND ProductId <> ISNULL(@ProductId, 0))
        BEGIN;
            THROW 50005, N'Another product already uses this SKU.', 1;
        END

        IF @ProductId IS NULL
        BEGIN
            INSERT dbo.Product (Sku, Name, CategoryId, UnitPrice, ReorderLevel, IsActive)
            VALUES (@Sku, @Name, @CategoryId, @UnitPrice, @ReorderLevel, @IsActive);

            SET @ProductId = CAST(SCOPE_IDENTITY() AS INT);
        END
        ELSE
        BEGIN
            UPDATE dbo.Product
            SET Sku = @Sku, Name = @Name, CategoryId = @CategoryId, UnitPrice = @UnitPrice,
                ReorderLevel = @ReorderLevel, IsActive = @IsActive, ModifiedAt = SYSUTCDATETIME()
            WHERE ProductId = @ProductId
              AND (@RowVersion IS NULL OR RowVer = @RowVersion);

            IF @@ROWCOUNT = 0
            BEGIN
                IF EXISTS (SELECT 1 FROM dbo.Product WHERE ProductId = @ProductId)
                BEGIN;
                    THROW 50003, N'This product was changed by another user. Reload it and try again.', 1;
                END;
                THROW 50004, N'Product not found.', 1;
            END
        END

        COMMIT TRANSACTION;

        SELECT @ProductId AS ProductId;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        EXEC dbo.usp_ErrorLog_Write;
        THROW;
    END CATCH
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_StockMovement_ListByProduct
    @ProductId INT,
    @Top       INT = 50
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP (@Top)
           m.StockMovementId, m.ProductId, m.WarehouseId, w.Code AS WarehouseCode,
           m.MovementType, m.QuantityChange, m.ReservedChange,
           m.Reference, m.Reason, m.CreatedBy, m.CreatedAt
    FROM dbo.StockMovement m
    JOIN dbo.Warehouse w ON w.WarehouseId = m.WarehouseId
    WHERE m.ProductId = @ProductId
    ORDER BY m.CreatedAt DESC, m.StockMovementId DESC;
END
GO
