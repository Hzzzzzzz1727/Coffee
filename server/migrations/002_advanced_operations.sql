IF OBJECT_ID('dbo.ProductToppings','U') IS NULL
CREATE TABLE dbo.ProductToppings(Id uniqueidentifier NOT NULL DEFAULT NEWID() PRIMARY KEY,ProductId uniqueidentifier NOT NULL REFERENCES dbo.Products(Id),Name nvarchar(120) NOT NULL,Price int NOT NULL DEFAULT 0 CHECK(Price>=0),IsActive bit NOT NULL DEFAULT 1,CONSTRAINT UQ_ProductTopping UNIQUE(ProductId,Name));
IF OBJECT_ID('dbo.OrderItemToppings','U') IS NULL
CREATE TABLE dbo.OrderItemToppings(Id uniqueidentifier NOT NULL DEFAULT NEWID() PRIMARY KEY,OrderItemId uniqueidentifier NOT NULL REFERENCES dbo.OrderItems(Id),ToppingId uniqueidentifier NULL REFERENCES dbo.ProductToppings(Id),Name nvarchar(120) NOT NULL,Price int NOT NULL DEFAULT 0,Quantity smallint NOT NULL DEFAULT 1);
IF OBJECT_ID('dbo.Customers','U') IS NULL
CREATE TABLE dbo.Customers(Id uniqueidentifier NOT NULL DEFAULT NEWID() PRIMARY KEY,StoreId uniqueidentifier NOT NULL REFERENCES dbo.Stores(Id),Phone nvarchar(30) NOT NULL,FullName nvarchar(150) NOT NULL,Points int NOT NULL DEFAULT 0,TotalSpent bigint NOT NULL DEFAULT 0,CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),CONSTRAINT UQ_CustomerPhone UNIQUE(StoreId,Phone));
IF OBJECT_ID('dbo.Promotions','U') IS NULL
CREATE TABLE dbo.Promotions(Id uniqueidentifier NOT NULL DEFAULT NEWID() PRIMARY KEY,StoreId uniqueidentifier NOT NULL REFERENCES dbo.Stores(Id),Code nvarchar(40) NOT NULL,Name nvarchar(150) NOT NULL,DiscountType nvarchar(20) NOT NULL CHECK(DiscountType IN ('percent','fixed')),Value int NOT NULL CHECK(Value>0),MinimumOrder int NOT NULL DEFAULT 0,MaximumDiscount int NULL,StartsAt datetime2 NULL,EndsAt datetime2 NULL,IsActive bit NOT NULL DEFAULT 1,CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),CONSTRAINT UQ_PromotionCode UNIQUE(StoreId,Code));
IF OBJECT_ID('dbo.WorkShifts','U') IS NULL
CREATE TABLE dbo.WorkShifts(Id uniqueidentifier NOT NULL DEFAULT NEWID() PRIMARY KEY,StoreId uniqueidentifier NOT NULL REFERENCES dbo.Stores(Id),OpenedBy uniqueidentifier NOT NULL REFERENCES dbo.Users(Id),OpenedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),OpeningCash int NOT NULL DEFAULT 0,ClosedBy uniqueidentifier NULL REFERENCES dbo.Users(Id),ClosedAt datetime2 NULL,ClosingCash int NULL,ExpectedCash int NULL,Variance int NULL,Note nvarchar(500) NULL,Status nvarchar(20) NOT NULL DEFAULT 'open' CHECK(Status IN ('open','closed')));
IF OBJECT_ID('dbo.OrderCancellations','U') IS NULL
CREATE TABLE dbo.OrderCancellations(Id uniqueidentifier NOT NULL DEFAULT NEWID() PRIMARY KEY,OrderId uniqueidentifier NOT NULL REFERENCES dbo.Orders(Id),OrderItemId uniqueidentifier NULL REFERENCES dbo.OrderItems(Id),CancelledBy uniqueidentifier NULL REFERENCES dbo.Users(Id),Reason nvarchar(300) NOT NULL,Amount int NOT NULL DEFAULT 0,CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME());
IF OBJECT_ID('dbo.PushSubscriptions','U') IS NULL
CREATE TABLE dbo.PushSubscriptions(Id uniqueidentifier NOT NULL DEFAULT NEWID() PRIMARY KEY,UserId uniqueidentifier NOT NULL REFERENCES dbo.Users(Id),Endpoint nvarchar(1000) NOT NULL,P256dh nvarchar(300) NOT NULL,Auth nvarchar(300) NOT NULL,CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME());
IF OBJECT_ID('dbo.PrinterSettings','U') IS NULL
CREATE TABLE dbo.PrinterSettings(StoreId uniqueidentifier NOT NULL PRIMARY KEY REFERENCES dbo.Stores(Id),PrinterName nvarchar(250) NOT NULL DEFAULT N'',AutoPrint bit NOT NULL DEFAULT 0,OpenCashDrawer bit NOT NULL DEFAULT 0,UpdatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME());
IF OBJECT_ID('dbo.SystemSettings','U') IS NULL
CREATE TABLE dbo.SystemSettings([Key] nvarchar(100) NOT NULL PRIMARY KEY,[Value] nvarchar(max) NOT NULL,UpdatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME());
IF COL_LENGTH('dbo.Orders','CustomerId') IS NULL ALTER TABLE dbo.Orders ADD CustomerId uniqueidentifier NULL REFERENCES dbo.Customers(Id);
IF COL_LENGTH('dbo.Orders','PromotionId') IS NULL ALTER TABLE dbo.Orders ADD PromotionId uniqueidentifier NULL REFERENCES dbo.Promotions(Id);
IF COL_LENGTH('dbo.Orders','ShiftId') IS NULL ALTER TABLE dbo.Orders ADD ShiftId uniqueidentifier NULL REFERENCES dbo.WorkShifts(Id);
IF COL_LENGTH('dbo.Orders','ClosedBy') IS NULL ALTER TABLE dbo.Orders ADD ClosedBy uniqueidentifier NULL REFERENCES dbo.Users(Id);
IF COL_LENGTH('dbo.Orders','PointsRedeemed') IS NULL ALTER TABLE dbo.Orders ADD PointsRedeemed int NOT NULL CONSTRAINT DF_Orders_PointsRedeemed DEFAULT 0;
IF COL_LENGTH('dbo.Orders','LoyaltyDiscount') IS NULL ALTER TABLE dbo.Orders ADD LoyaltyDiscount int NOT NULL CONSTRAINT DF_Orders_LoyaltyDiscount DEFAULT 0;
IF COL_LENGTH('dbo.OrderItems','PreparedBy') IS NULL ALTER TABLE dbo.OrderItems ADD PreparedBy uniqueidentifier NULL REFERENCES dbo.Users(Id);
IF COL_LENGTH('dbo.OrderItems','ReadyAt') IS NULL ALTER TABLE dbo.OrderItems ADD ReadyAt datetime2 NULL;
IF COL_LENGTH('dbo.OrderItems','ServedAt') IS NULL ALTER TABLE dbo.OrderItems ADD ServedAt datetime2 NULL;
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name='UX_PushSubscriptions_Endpoint') CREATE UNIQUE INDEX UX_PushSubscriptions_Endpoint ON dbo.PushSubscriptions(Endpoint);
