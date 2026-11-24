/*
    Demo data for local development and demos. NOT for production.
    The deployer only runs this when --seed-demo is passed and dbo.Product is empty.
    Mirrors StockFlow.Data.InMemory.DemoData so both data providers look the same.
*/

SET IDENTITY_INSERT dbo.Product ON;
INSERT dbo.Product (ProductId, Sku, Name, CategoryId, UnitPrice, ReorderLevel)
VALUES ( 1, N'AUD-001', N'Wireless Earbuds Pro',            1, 1299.00,  40),
       ( 2, N'AUD-002', N'Bluetooth Speaker Mini',          1,  649.00,  30),
       ( 3, N'AUD-003', N'Noise Cancelling Headphones',     1, 3499.00,  15),
       ( 4, N'CMP-001', N'USB-C Docking Station',           2, 1899.00,  20),
       ( 5, N'CMP-002', N'Wireless Keyboard and Mouse Set', 2,  799.00,  25),
       ( 6, N'CMP-003', N'27-inch 4K Monitor',              2, 6499.00,   8),
       ( 7, N'CMP-004', N'1TB Portable SSD',                2, 1599.00,  20),
       ( 8, N'MOB-001', N'65W GaN Fast Charger',            3,  499.00,  60),
       ( 9, N'MOB-002', N'USB-C to Lightning Cable 1m',     3,  199.00, 100),
       (10, N'MOB-003', N'10000mAh Power Bank',             3,  449.00,  50),
       (11, N'MOB-004', N'MagSafe Phone Case',              3,  349.00,  40),
       (12, N'SHM-001', N'Smart Plug (2-pack)',             4,  399.00,  30),
       (13, N'SHM-002', N'Wi-Fi Security Camera',           4, 1199.00,  15),
       (14, N'SHM-003', N'Smart LED Bulb E27',              4,  149.00,  80),
       (15, N'GAM-001', N'Wireless Game Controller',        5,  999.00,  20),
       (16, N'GAM-002', N'Gaming Headset RGB',              5, 1499.00,  12);
SET IDENTITY_INSERT dbo.Product OFF;
GO

SET IDENTITY_INSERT dbo.Customer ON;
INSERT dbo.Customer (CustomerId, AccountCode, Name, Email)
VALUES (1, N'GHR001', N'Gadget Hub Retail',          N'orders@gadgethub.example'),
       (2, N'NGE001', N'Northgate Electronics',      N'buying@northgate.example'),
       (3, N'CTS001', N'Coastal Tech Supplies',      N'procurement@coastaltech.example'),
       (4, N'HOS001', N'Highveld Office Solutions',  N'stock@highveldoffice.example'),
       (5, N'BSS001', N'Bright Spark Stores',        N'orders@brightspark.example'),
       (6, N'MMK001', N'Metro Mobile Kiosks',        N'supply@metromobile.example');
SET IDENTITY_INSERT dbo.Customer OFF;
GO

/* Opening stock is posted as receipts so the audit trail adds up. */
DECLARE @Opening TABLE (ProductId INT, WarehouseId INT, Quantity INT);
INSERT @Opening (ProductId, WarehouseId, Quantity)
VALUES ( 1,1,120),( 1,2, 35),( 1,3,20),
       ( 2,1, 18),( 2,2, 10),
       ( 3,1, 25),( 3,2,  8),( 3,3, 4),
       ( 4,1, 40),( 4,2, 12),( 4,3, 6),
       ( 5,1, 60),( 5,2, 15),( 5,3,10),
       ( 6,1,  5),( 6,2,  2),
       ( 7,1, 45),( 7,2, 20),( 7,3,10),
       ( 8,1,200),( 8,2, 80),( 8,3,50),
       ( 9,1, 70),( 9,2, 20),( 9,3, 5),
       (10,1, 90),(10,2, 40),(10,3,25),
       (11,1, 55),(11,2, 25),(11,3,12),
       (12,1, 35),(12,2, 10),(12,3, 8),
       (13,1,  9),(13,2,  3),
       (14,1,300),(14,2,120),(14,3,60),
       (15,1, 30),(15,2, 12),(15,3, 6),
       (16,1, 14),(16,2,  4),(16,3, 2);

INSERT dbo.StockLevel (ProductId, WarehouseId, QuantityOnHand)
SELECT ProductId, WarehouseId, Quantity FROM @Opening;

INSERT dbo.StockMovement (ProductId, WarehouseId, MovementType, QuantityChange, Reference, Reason, CreatedBy)
SELECT ProductId, WarehouseId, 1, Quantity, N'OPENING', N'Opening balance', N'seed'
FROM @Opening;
GO

/* Sample orders, created through the same procedures the applications use. */
DECLARE @Lines dbo.OrderLineList, @Id INT;
DECLARE @Created TABLE (SalesOrderId INT);

INSERT @Lines VALUES (1, 10), (8, 20);
INSERT @Created EXEC dbo.usp_Order_Create @CustomerId = 1, @WarehouseId = 1, @User = N'seed', @Lines = @Lines;
SELECT @Id = MAX(SalesOrderId) FROM @Created;
EXEC dbo.usp_Order_Allocate @SalesOrderId = @Id, @User = N'seed';

DELETE @Lines; DELETE @Created;
INSERT @Lines VALUES (4, 5), (5, 5);
INSERT @Created EXEC dbo.usp_Order_Create @CustomerId = 2, @WarehouseId = 1, @User = N'seed', @Lines = @Lines;

DELETE @Lines; DELETE @Created;
INSERT @Lines VALUES (10, 5), (14, 20);
INSERT @Created EXEC dbo.usp_Order_Create @CustomerId = 3, @WarehouseId = 3, @User = N'seed', @Lines = @Lines;
SELECT @Id = MAX(SalesOrderId) FROM @Created;
EXEC dbo.usp_Order_Allocate @SalesOrderId = @Id, @User = N'seed';
EXEC dbo.usp_Order_Dispatch @SalesOrderId = @Id, @User = N'seed';

DELETE @Lines; DELETE @Created;
INSERT @Lines VALUES (6, 2);
INSERT @Created EXEC dbo.usp_Order_Create @CustomerId = 4, @WarehouseId = 2, @Notes = N'Deliver to reception', @User = N'seed', @Lines = @Lines;

DELETE @Lines; DELETE @Created;
INSERT @Lines VALUES (2, 30);
INSERT @Created EXEC dbo.usp_Order_Create @CustomerId = 5, @WarehouseId = 1, @Notes = N'Promotion stock - more than JHB holds', @User = N'seed', @Lines = @Lines;
GO
