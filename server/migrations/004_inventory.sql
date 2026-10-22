IF OBJECT_ID('dbo.Suppliers','U') IS NULL
CREATE TABLE dbo.Suppliers(
  Id uniqueidentifier NOT NULL DEFAULT NEWID() PRIMARY KEY,
  StoreId uniqueidentifier NOT NULL REFERENCES dbo.Stores(Id),
  Name nvarchar(150) NOT NULL,
  Phone nvarchar(30) NULL,
  Address nvarchar(300) NULL,
  Note nvarchar(500) NULL,
  IsActive bit NOT NULL DEFAULT 1,
  CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME()
);

IF COL_LENGTH('dbo.Ingredients','Code') IS NULL ALTER TABLE dbo.Ingredients ADD Code nvarchar(40) NULL;
IF COL_LENGTH('dbo.Ingredients','Category') IS NULL ALTER TABLE dbo.Ingredients ADD Category nvarchar(100) NULL;
IF COL_LENGTH('dbo.Ingredients','AverageCost') IS NULL ALTER TABLE dbo.Ingredients ADD AverageCost decimal(18,4) NOT NULL CONSTRAINT DF_Ingredients_AverageCost DEFAULT 0;
IF COL_LENGTH('dbo.Ingredients','IsActive') IS NULL ALTER TABLE dbo.Ingredients ADD IsActive bit NOT NULL CONSTRAINT DF_Ingredients_IsActive DEFAULT 1;
GO
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name='UX_Ingredients_Code') CREATE UNIQUE INDEX UX_Ingredients_Code ON dbo.Ingredients(StoreId,Code) WHERE Code IS NOT NULL;

IF OBJECT_ID('dbo.StockReceipts','U') IS NULL
CREATE TABLE dbo.StockReceipts(
  Id uniqueidentifier NOT NULL DEFAULT NEWID() PRIMARY KEY,
  StoreId uniqueidentifier NOT NULL REFERENCES dbo.Stores(Id),
  ReceiptNumber bigint IDENTITY(1000,1) NOT NULL UNIQUE,
  SupplierId uniqueidentifier NULL REFERENCES dbo.Suppliers(Id),
  Status nvarchar(20) NOT NULL DEFAULT 'draft' CHECK(Status IN ('draft','confirmed','cancelled')),
  Note nvarchar(500) NULL,
  TotalCost decimal(18,2) NOT NULL DEFAULT 0,
  CreatedBy uniqueidentifier NULL REFERENCES dbo.Users(Id),
  CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
  ConfirmedBy uniqueidentifier NULL REFERENCES dbo.Users(Id),
  ConfirmedAt datetime2 NULL
);
GO
IF OBJECT_ID('dbo.StockReceiptItems','U') IS NULL
CREATE TABLE dbo.StockReceiptItems(
  Id uniqueidentifier NOT NULL DEFAULT NEWID() PRIMARY KEY,
  ReceiptId uniqueidentifier NOT NULL REFERENCES dbo.StockReceipts(Id),
  IngredientId uniqueidentifier NOT NULL REFERENCES dbo.Ingredients(Id),
  Quantity decimal(18,3) NOT NULL CHECK(Quantity>0),
  UnitCost decimal(18,4) NOT NULL CHECK(UnitCost>=0)
);

IF OBJECT_ID('dbo.RecipeComponents','U') IS NULL
CREATE TABLE dbo.RecipeComponents(
  Id uniqueidentifier NOT NULL DEFAULT NEWID() PRIMARY KEY,
  ProductId uniqueidentifier NULL REFERENCES dbo.Products(Id),
  ToppingId uniqueidentifier NULL REFERENCES dbo.ProductToppings(Id),
  IngredientId uniqueidentifier NOT NULL REFERENCES dbo.Ingredients(Id),
  Quantity decimal(18,3) NOT NULL CHECK(Quantity>0),
  CONSTRAINT CK_RecipeOwner CHECK((ProductId IS NOT NULL AND ToppingId IS NULL) OR (ProductId IS NULL AND ToppingId IS NOT NULL))
);
GO
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name='UX_Recipe_ProductIngredient') CREATE UNIQUE INDEX UX_Recipe_ProductIngredient ON dbo.RecipeComponents(ProductId,IngredientId) WHERE ProductId IS NOT NULL;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name='UX_Recipe_ToppingIngredient') CREATE UNIQUE INDEX UX_Recipe_ToppingIngredient ON dbo.RecipeComponents(ToppingId,IngredientId) WHERE ToppingId IS NOT NULL;

IF COL_LENGTH('dbo.StockMovements','MovementType') IS NULL ALTER TABLE dbo.StockMovements ADD MovementType nvarchar(30) NOT NULL CONSTRAINT DF_StockMovements_Type DEFAULT N'adjustment';
IF COL_LENGTH('dbo.StockMovements','ReferenceType') IS NULL ALTER TABLE dbo.StockMovements ADD ReferenceType nvarchar(30) NULL;
IF COL_LENGTH('dbo.StockMovements','ReferenceId') IS NULL ALTER TABLE dbo.StockMovements ADD ReferenceId uniqueidentifier NULL;
IF COL_LENGTH('dbo.StockMovements','UnitCost') IS NULL ALTER TABLE dbo.StockMovements ADD UnitCost decimal(18,4) NULL;
IF COL_LENGTH('dbo.StockMovements','BalanceAfter') IS NULL ALTER TABLE dbo.StockMovements ADD BalanceAfter decimal(18,3) NULL;
GO
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name='UX_Stock_OrderIngredient') CREATE UNIQUE INDEX UX_Stock_OrderIngredient ON dbo.StockMovements(ReferenceId,IngredientId) WHERE ReferenceType=N'order_item' AND MovementType=N'consumption';

IF COL_LENGTH('dbo.OrderItems','InventoryDeducted') IS NULL ALTER TABLE dbo.OrderItems ADD InventoryDeducted bit NOT NULL CONSTRAINT DF_OrderItems_InventoryDeducted DEFAULT 0;
IF COL_LENGTH('dbo.OrderItems','InventoryDeductedAt') IS NULL ALTER TABLE dbo.OrderItems ADD InventoryDeductedAt datetime2 NULL;
GO

IF OBJECT_ID('dbo.InventoryCounts','U') IS NULL
CREATE TABLE dbo.InventoryCounts(
  Id uniqueidentifier NOT NULL DEFAULT NEWID() PRIMARY KEY,
  StoreId uniqueidentifier NOT NULL REFERENCES dbo.Stores(Id),
  CountNumber bigint IDENTITY(1000,1) NOT NULL UNIQUE,
  Note nvarchar(500) NULL,
  CreatedBy uniqueidentifier NULL REFERENCES dbo.Users(Id),
  CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME()
);
IF OBJECT_ID('dbo.InventoryCountItems','U') IS NULL
CREATE TABLE dbo.InventoryCountItems(
  Id uniqueidentifier NOT NULL DEFAULT NEWID() PRIMARY KEY,
  CountId uniqueidentifier NOT NULL REFERENCES dbo.InventoryCounts(Id),
  IngredientId uniqueidentifier NOT NULL REFERENCES dbo.Ingredients(Id),
  SystemQuantity decimal(18,3) NOT NULL,
  ActualQuantity decimal(18,3) NOT NULL,
  Difference decimal(18,3) NOT NULL
);

GO
CREATE OR ALTER TRIGGER dbo.TR_Orders_InventoryFallback ON dbo.Orders AFTER UPDATE AS
BEGIN
  SET NOCOUNT ON;
  DECLARE @moves TABLE(IngredientId uniqueidentifier,Qty decimal(18,3));
  ;WITH PendingItems AS(
    SELECT oi.Id,oi.ProductId,oi.Quantity,COALESCE(oi.PreparedBy,i.ClosedBy) UserId
    FROM dbo.OrderItems oi JOIN inserted i ON i.Id=oi.OrderId
    WHERE i.Status IN ('preparing','ready','paid') AND oi.Status<>'cancelled' AND oi.InventoryDeducted=0
  ), Required AS(
    SELECT p.Id ItemId,r.IngredientId,r.Quantity*CONVERT(decimal(18,3),p.Quantity) Qty,p.UserId FROM PendingItems p JOIN dbo.RecipeComponents r ON r.ProductId=p.ProductId
    UNION ALL
    SELECT p.Id,r.IngredientId,r.Quantity*CONVERT(decimal(18,3),p.Quantity)*CONVERT(decimal(18,3),t.Quantity),p.UserId FROM PendingItems p JOIN dbo.OrderItemToppings t ON t.OrderItemId=p.Id JOIN dbo.RecipeComponents r ON r.ToppingId=t.ToppingId
  ), Totals AS(SELECT ItemId,IngredientId,SUM(Qty) Qty,UserId FROM Required GROUP BY ItemId,IngredientId,UserId)
  INSERT dbo.StockMovements(IngredientId,QuantityChange,Reason,CreatedBy,MovementType,ReferenceType,ReferenceId,UnitCost,BalanceAfter)
  OUTPUT INSERTED.IngredientId,-INSERTED.QuantityChange INTO @moves
  SELECT t.IngredientId,-t.Qty,N'Trừ kho dự phòng khi đóng đơn',t.UserId,N'consumption',N'order_item',t.ItemId,g.AverageCost,g.Quantity-t.Qty FROM Totals t JOIN dbo.Ingredients g ON g.Id=t.IngredientId;
  ;WITH Needed AS(SELECT IngredientId,SUM(Qty) Qty FROM @moves GROUP BY IngredientId)
  UPDATE g SET Quantity=g.Quantity-n.Qty FROM dbo.Ingredients g JOIN Needed n ON n.IngredientId=g.Id;
  UPDATE oi SET InventoryDeducted=1,InventoryDeductedAt=SYSUTCDATETIME() FROM dbo.OrderItems oi JOIN inserted i ON i.Id=oi.OrderId WHERE i.Status IN ('preparing','ready','paid') AND oi.Status<>'cancelled' AND oi.InventoryDeducted=0;
END;
GO
