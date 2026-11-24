/*
    0002_CreateTypes
    Table-valued parameter used to send all order lines to SQL Server in a
    single round trip (instead of one INSERT per line).
*/

CREATE TYPE dbo.OrderLineList AS TABLE
(
    ProductId INT NOT NULL PRIMARY KEY,
    Quantity  INT NOT NULL
);
GO
