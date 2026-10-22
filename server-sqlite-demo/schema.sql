PRAGMA foreign_keys = ON;
PRAGMA journal_mode = WAL;
CREATE TABLE IF NOT EXISTS Stores(Id TEXT PRIMARY KEY,Name TEXT NOT NULL,Address TEXT,Timezone TEXT NOT NULL DEFAULT 'Asia/Ho_Chi_Minh',CreatedAt TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS Users(Id TEXT PRIMARY KEY,StoreId TEXT,FullName TEXT NOT NULL,Email TEXT NOT NULL COLLATE NOCASE UNIQUE,PasswordHash TEXT NOT NULL,Role TEXT NOT NULL,IsActive INTEGER NOT NULL DEFAULT 1,CreatedAt TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS Areas(Id TEXT PRIMARY KEY,StoreId TEXT NOT NULL,Name TEXT NOT NULL,Position INTEGER NOT NULL DEFAULT 0,CanvasWidth INTEGER NOT NULL DEFAULT 1000,CanvasHeight INTEGER NOT NULL DEFAULT 600);
CREATE TABLE IF NOT EXISTS DiningTables(Id TEXT PRIMARY KEY,AreaId TEXT NOT NULL,Code TEXT NOT NULL,Seats INTEGER NOT NULL,Status TEXT NOT NULL DEFAULT 'available',PositionX INTEGER NOT NULL DEFAULT 0,PositionY INTEGER NOT NULL DEFAULT 0,Width INTEGER NOT NULL DEFAULT 190,Height INTEGER NOT NULL DEFAULT 120,Shape TEXT NOT NULL DEFAULT 'rectangle',UNIQUE(AreaId,Code));
CREATE TABLE IF NOT EXISTS Categories(Id TEXT PRIMARY KEY,StoreId TEXT NOT NULL,Name TEXT NOT NULL,Position INTEGER NOT NULL DEFAULT 0,IsActive INTEGER NOT NULL DEFAULT 1);
CREATE TABLE IF NOT EXISTS Products(Id TEXT PRIMARY KEY,CategoryId TEXT NOT NULL,Name TEXT NOT NULL,SKU TEXT,BasePrice INTEGER NOT NULL,ImageUrl TEXT,IsActive INTEGER NOT NULL DEFAULT 1,IsAvailable INTEGER NOT NULL DEFAULT 1,CreatedAt TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS ProductToppings(Id TEXT PRIMARY KEY,ProductId TEXT NOT NULL,Name TEXT NOT NULL,Price INTEGER NOT NULL DEFAULT 0,IsActive INTEGER NOT NULL DEFAULT 1,UNIQUE(ProductId,Name));
CREATE TABLE IF NOT EXISTS Orders(Id TEXT PRIMARY KEY,StoreId TEXT NOT NULL,TableId TEXT,OrderNumber INTEGER NOT NULL UNIQUE,Status TEXT NOT NULL DEFAULT 'draft',GuestCount INTEGER NOT NULL DEFAULT 1,Note TEXT,Subtotal INTEGER NOT NULL DEFAULT 0,Discount INTEGER NOT NULL DEFAULT 0,LoyaltyDiscount INTEGER NOT NULL DEFAULT 0,Total INTEGER NOT NULL DEFAULT 0,OpenedBy TEXT,OpenedAt TEXT NOT NULL,ClosedAt TEXT,BillRequestedAt TEXT,CustomerId TEXT,PromotionId TEXT,ShiftId TEXT,ClosedBy TEXT,PointsRedeemed INTEGER NOT NULL DEFAULT 0);
CREATE TABLE IF NOT EXISTS OrderItems(Id TEXT PRIMARY KEY,OrderId TEXT NOT NULL,ProductId TEXT,ProductName TEXT NOT NULL,UnitPrice INTEGER NOT NULL,Quantity INTEGER NOT NULL,Note TEXT,Status TEXT NOT NULL DEFAULT 'sent',CreatedAt TEXT NOT NULL,PreparedBy TEXT,ReadyAt TEXT,ServedAt TEXT,InventoryDeducted INTEGER NOT NULL DEFAULT 0,InventoryDeductedAt TEXT);
CREATE TABLE IF NOT EXISTS OrderItemToppings(Id TEXT PRIMARY KEY,OrderItemId TEXT NOT NULL,ToppingId TEXT,Name TEXT NOT NULL,Price INTEGER NOT NULL DEFAULT 0,Quantity INTEGER NOT NULL DEFAULT 1);
CREATE TABLE IF NOT EXISTS Payments(Id TEXT PRIMARY KEY,OrderId TEXT NOT NULL,Amount INTEGER NOT NULL,Method TEXT NOT NULL,Reference TEXT,ReceivedBy TEXT,PaidAt TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS Customers(Id TEXT PRIMARY KEY,StoreId TEXT NOT NULL,Phone TEXT NOT NULL,FullName TEXT NOT NULL,Points INTEGER NOT NULL DEFAULT 0,TotalSpent INTEGER NOT NULL DEFAULT 0,CreatedAt TEXT NOT NULL,UNIQUE(StoreId,Phone));
CREATE TABLE IF NOT EXISTS Promotions(Id TEXT PRIMARY KEY,StoreId TEXT NOT NULL,Code TEXT NOT NULL COLLATE NOCASE,Name TEXT NOT NULL,DiscountType TEXT NOT NULL,Value INTEGER NOT NULL,MinimumOrder INTEGER NOT NULL DEFAULT 0,MaximumDiscount INTEGER,StartsAt TEXT,EndsAt TEXT,IsActive INTEGER NOT NULL DEFAULT 1,CreatedAt TEXT NOT NULL,UNIQUE(StoreId,Code));
CREATE TABLE IF NOT EXISTS WorkShifts(Id TEXT PRIMARY KEY,StoreId TEXT NOT NULL,OpenedBy TEXT NOT NULL,OpenedAt TEXT NOT NULL,OpeningCash INTEGER NOT NULL DEFAULT 0,ClosedBy TEXT,ClosedAt TEXT,ClosingCash INTEGER,ExpectedCash INTEGER,Variance INTEGER,Note TEXT,Status TEXT NOT NULL DEFAULT 'open');
CREATE TABLE IF NOT EXISTS OrderCancellations(Id TEXT PRIMARY KEY,OrderId TEXT NOT NULL,OrderItemId TEXT,CancelledBy TEXT,Reason TEXT NOT NULL,Amount INTEGER NOT NULL DEFAULT 0,CreatedAt TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS PaymentSettings(StoreId TEXT PRIMARY KEY,BankId TEXT NOT NULL,AccountNumber TEXT NOT NULL,AccountName TEXT NOT NULL,UpdatedAt TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS PrinterSettings(StoreId TEXT PRIMARY KEY,PrinterName TEXT NOT NULL DEFAULT '',AutoPrint INTEGER NOT NULL DEFAULT 0,OpenCashDrawer INTEGER NOT NULL DEFAULT 0,UpdatedAt TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS PushSubscriptions(Id TEXT PRIMARY KEY,UserId TEXT NOT NULL,Endpoint TEXT NOT NULL UNIQUE,P256dh TEXT NOT NULL,Auth TEXT NOT NULL,CreatedAt TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS SystemSettings(Key TEXT PRIMARY KEY,Value TEXT NOT NULL,UpdatedAt TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS Ingredients(Id TEXT PRIMARY KEY,StoreId TEXT NOT NULL,Code TEXT,Name TEXT NOT NULL,Category TEXT,Unit TEXT NOT NULL,Quantity REAL NOT NULL DEFAULT 0,ReorderLevel REAL NOT NULL DEFAULT 0,AverageCost REAL NOT NULL DEFAULT 0,IsActive INTEGER NOT NULL DEFAULT 1,CreatedAt TEXT NOT NULL);
CREATE UNIQUE INDEX IF NOT EXISTS UX_Ingredients_Code ON Ingredients(StoreId,Code) WHERE Code IS NOT NULL;
CREATE TABLE IF NOT EXISTS Suppliers(Id TEXT PRIMARY KEY,StoreId TEXT NOT NULL,Name TEXT NOT NULL,Phone TEXT,Address TEXT,Note TEXT,IsActive INTEGER NOT NULL DEFAULT 1,CreatedAt TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS StockReceipts(Id TEXT PRIMARY KEY,StoreId TEXT NOT NULL,ReceiptNumber INTEGER NOT NULL UNIQUE,Status TEXT NOT NULL DEFAULT 'draft',SupplierId TEXT,Note TEXT,TotalCost REAL NOT NULL DEFAULT 0,CreatedBy TEXT,CreatedAt TEXT NOT NULL,ConfirmedBy TEXT,ConfirmedAt TEXT);
CREATE TABLE IF NOT EXISTS StockReceiptItems(Id TEXT PRIMARY KEY,ReceiptId TEXT NOT NULL,IngredientId TEXT NOT NULL,Quantity REAL NOT NULL,UnitCost REAL NOT NULL);
CREATE TABLE IF NOT EXISTS RecipeComponents(Id TEXT PRIMARY KEY,ProductId TEXT,ToppingId TEXT,IngredientId TEXT NOT NULL,Quantity REAL NOT NULL);
CREATE UNIQUE INDEX IF NOT EXISTS UX_Recipe_ProductIngredient ON RecipeComponents(ProductId,IngredientId) WHERE ProductId IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS UX_Recipe_ToppingIngredient ON RecipeComponents(ToppingId,IngredientId) WHERE ToppingId IS NOT NULL;
CREATE TABLE IF NOT EXISTS StockMovements(Id TEXT PRIMARY KEY,IngredientId TEXT NOT NULL,QuantityChange REAL NOT NULL,Reason TEXT NOT NULL,CreatedBy TEXT,MovementType TEXT NOT NULL DEFAULT 'adjustment',ReferenceType TEXT,ReferenceId TEXT,UnitCost REAL,BalanceAfter REAL,CreatedAt TEXT NOT NULL);
CREATE UNIQUE INDEX IF NOT EXISTS UX_Stock_OrderIngredient ON StockMovements(ReferenceId,IngredientId) WHERE ReferenceType='order_item' AND MovementType='consumption';
CREATE TABLE IF NOT EXISTS InventoryCounts(Id TEXT PRIMARY KEY,StoreId TEXT NOT NULL,CountNumber INTEGER NOT NULL UNIQUE,Note TEXT,CreatedBy TEXT,CreatedAt TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS InventoryCountItems(Id TEXT PRIMARY KEY,CountId TEXT NOT NULL,IngredientId TEXT NOT NULL,SystemQuantity REAL NOT NULL,ActualQuantity REAL NOT NULL,Difference REAL NOT NULL);
CREATE INDEX IF NOT EXISTS IX_Orders_Status ON Orders(Status,OpenedAt DESC);
CREATE INDEX IF NOT EXISTS IX_OrderItems_Order ON OrderItems(OrderId);
CREATE TRIGGER IF NOT EXISTS TR_Orders_InventoryFallback AFTER UPDATE OF Status ON Orders
WHEN NEW.Status IN ('preparing','ready','paid')
BEGIN
  INSERT INTO StockMovements(Id,IngredientId,QuantityChange,Reason,CreatedBy,MovementType,ReferenceType,ReferenceId,UnitCost,BalanceAfter,CreatedAt)
  SELECT lower(hex(randomblob(4)))||'-'||lower(hex(randomblob(2)))||'-4'||substr(lower(hex(randomblob(2))),2)||'-a'||substr(lower(hex(randomblob(2))),2)||'-'||lower(hex(randomblob(6))),q.IngredientId,-SUM(q.Qty),'Trừ kho dự phòng khi đóng đơn',COALESCE(MAX(q.UserId),NEW.ClosedBy),'consumption','order_item',q.ItemId,i.AverageCost,i.Quantity-SUM(q.Qty),strftime('%Y-%m-%dT%H:%M:%fZ','now')
  FROM (
    SELECT oi.Id ItemId,r.IngredientId,r.Quantity*oi.Quantity Qty,oi.PreparedBy UserId FROM OrderItems oi JOIN RecipeComponents r ON r.ProductId=oi.ProductId WHERE oi.OrderId=NEW.Id AND oi.Status<>'cancelled' AND oi.InventoryDeducted=0
    UNION ALL
    SELECT oi.Id,r.IngredientId,r.Quantity*oi.Quantity*t.Quantity,oi.PreparedBy FROM OrderItems oi JOIN OrderItemToppings t ON t.OrderItemId=oi.Id JOIN RecipeComponents r ON r.ToppingId=t.ToppingId WHERE oi.OrderId=NEW.Id AND oi.Status<>'cancelled' AND oi.InventoryDeducted=0
  ) q JOIN Ingredients i ON i.Id=q.IngredientId GROUP BY q.ItemId,q.IngredientId;
  UPDATE Ingredients SET Quantity=Quantity-COALESCE((SELECT SUM(q.Qty) FROM (
    SELECT oi.Id ItemId,r.IngredientId,r.Quantity*oi.Quantity Qty FROM OrderItems oi JOIN RecipeComponents r ON r.ProductId=oi.ProductId WHERE oi.OrderId=NEW.Id AND oi.Status<>'cancelled' AND oi.InventoryDeducted=0
    UNION ALL
    SELECT oi.Id,r.IngredientId,r.Quantity*oi.Quantity*t.Quantity FROM OrderItems oi JOIN OrderItemToppings t ON t.OrderItemId=oi.Id JOIN RecipeComponents r ON r.ToppingId=t.ToppingId WHERE oi.OrderId=NEW.Id AND oi.Status<>'cancelled' AND oi.InventoryDeducted=0
  ) q WHERE q.IngredientId=Ingredients.Id),0)
  WHERE Id IN (SELECT IngredientId FROM StockMovements WHERE ReferenceType='order_item' AND ReferenceId IN (SELECT Id FROM OrderItems WHERE OrderId=NEW.Id AND InventoryDeducted=0));
  UPDATE OrderItems SET InventoryDeducted=1,InventoryDeductedAt=strftime('%Y-%m-%dT%H:%M:%fZ','now') WHERE OrderId=NEW.Id AND Status<>'cancelled' AND InventoryDeducted=0;
END;
