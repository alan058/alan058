/*
    0001_CreateSchema
    Core tables for StockFlow: reference data, products, stock and sales orders.

    Data integrity is enforced in the database as well as in the application:
    foreign keys, unique keys and CHECK constraints make sure that no code path
    (desktop app, web app, ad-hoc script) can leave stock in an impossible state.
*/

CREATE TABLE dbo.Category
(
    CategoryId   INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Category PRIMARY KEY,
    Name         NVARCHAR(100)     NOT NULL CONSTRAINT UQ_Category_Name UNIQUE
);
GO

CREATE TABLE dbo.Warehouse
(
    WarehouseId  INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Warehouse PRIMARY KEY,
    Code         NVARCHAR(10)      NOT NULL CONSTRAINT UQ_Warehouse_Code UNIQUE,
    Name         NVARCHAR(100)     NOT NULL,
    IsActive     BIT               NOT NULL CONSTRAINT DF_Warehouse_IsActive DEFAULT (1)
);
GO

CREATE TABLE dbo.Customer
(
    CustomerId   INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Customer PRIMARY KEY,
    AccountCode  NVARCHAR(20)      NOT NULL CONSTRAINT UQ_Customer_AccountCode UNIQUE,
    Name         NVARCHAR(200)     NOT NULL,
    Email        NVARCHAR(256)     NULL,
    IsActive     BIT               NOT NULL CONSTRAINT DF_Customer_IsActive DEFAULT (1)
);
GO

CREATE TABLE dbo.Product
(
    ProductId    INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Product PRIMARY KEY,
    Sku          NVARCHAR(32)      NOT NULL CONSTRAINT UQ_Product_Sku UNIQUE,
    Name         NVARCHAR(200)     NOT NULL,
    CategoryId   INT               NOT NULL CONSTRAINT FK_Product_Category REFERENCES dbo.Category (CategoryId),
    UnitPrice    DECIMAL(18,2)     NOT NULL CONSTRAINT CK_Product_UnitPrice CHECK (UnitPrice >= 0),
    ReorderLevel INT               NOT NULL CONSTRAINT DF_Product_ReorderLevel DEFAULT (0)
                                            CONSTRAINT CK_Product_ReorderLevel CHECK (ReorderLevel >= 0),
    IsActive     BIT               NOT NULL CONSTRAINT DF_Product_IsActive DEFAULT (1),
    CreatedAt    DATETIME2(0)      NOT NULL CONSTRAINT DF_Product_CreatedAt DEFAULT (SYSUTCDATETIME()),
    ModifiedAt   DATETIME2(0)      NOT NULL CONSTRAINT DF_Product_ModifiedAt DEFAULT (SYSUTCDATETIME()),
    RowVer       ROWVERSION        NOT NULL
);
GO

CREATE INDEX IX_Product_CategoryId ON dbo.Product (CategoryId);
GO

/* One row per product per warehouse. Available = OnHand - Reserved. */
CREATE TABLE dbo.StockLevel
(
    ProductId        INT NOT NULL CONSTRAINT FK_StockLevel_Product   REFERENCES dbo.Product (ProductId),
    WarehouseId      INT NOT NULL CONSTRAINT FK_StockLevel_Warehouse REFERENCES dbo.Warehouse (WarehouseId),
    QuantityOnHand   INT NOT NULL CONSTRAINT DF_StockLevel_OnHand   DEFAULT (0),
    QuantityReserved INT NOT NULL CONSTRAINT DF_StockLevel_Reserved DEFAULT (0),
    CONSTRAINT PK_StockLevel PRIMARY KEY (ProductId, WarehouseId),
    CONSTRAINT CK_StockLevel_OnHand          CHECK (QuantityOnHand >= 0),
    CONSTRAINT CK_StockLevel_Reserved        CHECK (QuantityReserved >= 0),
    CONSTRAINT CK_StockLevel_ReservedOnHand  CHECK (QuantityReserved <= QuantityOnHand)
);
GO

CREATE INDEX IX_StockLevel_WarehouseId ON dbo.StockLevel (WarehouseId) INCLUDE (QuantityOnHand, QuantityReserved);
GO

/* Status: 0 = Open, 1 = Allocated, 2 = Dispatched, 3 = Cancelled */
CREATE TABLE dbo.SalesOrder
(
    SalesOrderId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SalesOrder PRIMARY KEY,
    OrderNumber  AS (CONVERT(NVARCHAR(20), N'SO-' + RIGHT(N'000000' + CONVERT(NVARCHAR(10), SalesOrderId), 6))) PERSISTED,
    CustomerId   INT              NOT NULL CONSTRAINT FK_SalesOrder_Customer  REFERENCES dbo.Customer (CustomerId),
    WarehouseId  INT              NOT NULL CONSTRAINT FK_SalesOrder_Warehouse REFERENCES dbo.Warehouse (WarehouseId),
    Status       TINYINT          NOT NULL CONSTRAINT DF_SalesOrder_Status DEFAULT (0)
                                           CONSTRAINT CK_SalesOrder_Status CHECK (Status BETWEEN 0 AND 3),
    OrderDate    DATETIME2(0)     NOT NULL CONSTRAINT DF_SalesOrder_OrderDate DEFAULT (SYSUTCDATETIME()),
    RequiredDate DATE             NULL,
    Notes        NVARCHAR(500)    NULL,
    CreatedBy    NVARCHAR(100)    NOT NULL,
    ModifiedAt   DATETIME2(0)     NOT NULL CONSTRAINT DF_SalesOrder_ModifiedAt DEFAULT (SYSUTCDATETIME()),
    DispatchedAt DATETIME2(0)     NULL
);
GO

CREATE INDEX IX_SalesOrder_Status_OrderDate ON dbo.SalesOrder (Status, OrderDate DESC);
CREATE INDEX IX_SalesOrder_CustomerId       ON dbo.SalesOrder (CustomerId);
GO

CREATE TABLE dbo.SalesOrderLine
(
    SalesOrderLineId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SalesOrderLine PRIMARY KEY,
    SalesOrderId     INT           NOT NULL CONSTRAINT FK_SalesOrderLine_SalesOrder REFERENCES dbo.SalesOrder (SalesOrderId),
    ProductId        INT           NOT NULL CONSTRAINT FK_SalesOrderLine_Product    REFERENCES dbo.Product (ProductId),
    Quantity         INT           NOT NULL CONSTRAINT CK_SalesOrderLine_Quantity CHECK (Quantity > 0),
    UnitPrice        DECIMAL(18,2) NOT NULL CONSTRAINT CK_SalesOrderLine_UnitPrice CHECK (UnitPrice >= 0),
    CONSTRAINT UQ_SalesOrderLine_Order_Product UNIQUE (SalesOrderId, ProductId)
);
GO

CREATE INDEX IX_SalesOrderLine_ProductId ON dbo.SalesOrderLine (ProductId);
GO

/*
    Audit trail of every change to stock. QuantityChange is the effect on
    on-hand stock, ReservedChange the effect on reserved stock.
    MovementType: 1 = Receipt, 2 = Adjustment, 3 = Allocation, 4 = Release, 5 = Dispatch
*/
CREATE TABLE dbo.StockMovement
(
    StockMovementId BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_StockMovement PRIMARY KEY,
    ProductId       INT            NOT NULL CONSTRAINT FK_StockMovement_Product   REFERENCES dbo.Product (ProductId),
    WarehouseId     INT            NOT NULL CONSTRAINT FK_StockMovement_Warehouse REFERENCES dbo.Warehouse (WarehouseId),
    MovementType    TINYINT        NOT NULL CONSTRAINT CK_StockMovement_Type CHECK (MovementType BETWEEN 1 AND 5),
    QuantityChange  INT            NOT NULL,
    ReservedChange  INT            NOT NULL CONSTRAINT DF_StockMovement_ReservedChange DEFAULT (0),
    Reference       NVARCHAR(50)   NULL,
    Reason          NVARCHAR(200)  NULL,
    CreatedBy       NVARCHAR(100)  NOT NULL,
    CreatedAt       DATETIME2(3)   NOT NULL CONSTRAINT DF_StockMovement_CreatedAt DEFAULT (SYSUTCDATETIME())
);
GO

CREATE INDEX IX_StockMovement_Product_CreatedAt ON dbo.StockMovement (ProductId, CreatedAt DESC);
GO

/* Unexpected database errors are written here by the CATCH blocks of stored procedures. */
CREATE TABLE dbo.ErrorLog
(
    ErrorLogId     INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ErrorLog PRIMARY KEY,
    LoggedAt       DATETIME2(3)   NOT NULL CONSTRAINT DF_ErrorLog_LoggedAt DEFAULT (SYSUTCDATETIME()),
    ErrorNumber    INT            NOT NULL,
    ErrorSeverity  INT            NULL,
    ErrorState     INT            NULL,
    ErrorProcedure NVARCHAR(128)  NULL,
    ErrorLine      INT            NULL,
    ErrorMessage   NVARCHAR(4000) NOT NULL,
    LoginName      NVARCHAR(128)  NOT NULL CONSTRAINT DF_ErrorLog_LoginName DEFAULT (SUSER_SNAME())
);
GO
