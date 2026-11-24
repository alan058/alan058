/*
    0003_ReferenceData
    Reference data the application needs in every environment.
*/

SET IDENTITY_INSERT dbo.Category ON;
INSERT dbo.Category (CategoryId, Name)
VALUES (1, N'Audio'), (2, N'Computing'), (3, N'Mobile Accessories'), (4, N'Smart Home'), (5, N'Gaming');
SET IDENTITY_INSERT dbo.Category OFF;
GO

SET IDENTITY_INSERT dbo.Warehouse ON;
INSERT dbo.Warehouse (WarehouseId, Code, Name)
VALUES (1, N'JHB', N'Johannesburg Distribution Centre'),
       (2, N'CPT', N'Cape Town Depot'),
       (3, N'DBN', N'Durban Depot');
SET IDENTITY_INSERT dbo.Warehouse OFF;
GO
