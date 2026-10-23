/* Bộ tính năng quản trị mở rộng. Migration chỉ bổ sung, không xóa dữ liệu cũ. */
IF COL_LENGTH('dbo.Stores','Code') IS NULL ALTER TABLE dbo.Stores ADD Code nvarchar(30) NULL;
IF COL_LENGTH('dbo.Stores','IsActive') IS NULL ALTER TABLE dbo.Stores ADD IsActive bit NOT NULL CONSTRAINT DF_Stores_IsActive DEFAULT 1;
IF COL_LENGTH('dbo.Stores','TaxCode') IS NULL ALTER TABLE dbo.Stores ADD TaxCode nvarchar(30) NULL;
IF COL_LENGTH('dbo.Stores','InvoiceName') IS NULL ALTER TABLE dbo.Stores ADD InvoiceName nvarchar(200) NULL;
IF COL_LENGTH('dbo.Stores','InvoiceEmail') IS NULL ALTER TABLE dbo.Stores ADD InvoiceEmail nvarchar(200) NULL;
IF COL_LENGTH('dbo.Stores','VatRate') IS NULL ALTER TABLE dbo.Stores ADD VatRate decimal(5,2) NOT NULL CONSTRAINT DF_Stores_VatRate DEFAULT 0;
GO
IF COL_LENGTH('dbo.Orders','OrderType') IS NULL ALTER TABLE dbo.Orders ADD OrderType nvarchar(20) NOT NULL CONSTRAINT DF_Orders_OrderType DEFAULT N'dine_in';
IF COL_LENGTH('dbo.Orders','CustomerName') IS NULL ALTER TABLE dbo.Orders ADD CustomerName nvarchar(150) NULL;
IF COL_LENGTH('dbo.Orders','CustomerPhone') IS NULL ALTER TABLE dbo.Orders ADD CustomerPhone nvarchar(30) NULL;
IF COL_LENGTH('dbo.Orders','DeliveryAddress') IS NULL ALTER TABLE dbo.Orders ADD DeliveryAddress nvarchar(400) NULL;
IF COL_LENGTH('dbo.Orders','FulfillmentAt') IS NULL ALTER TABLE dbo.Orders ADD FulfillmentAt datetime2 NULL;
IF COL_LENGTH('dbo.Orders','TaxAmount') IS NULL ALTER TABLE dbo.Orders ADD TaxAmount int NOT NULL CONSTRAINT DF_Orders_TaxAmount DEFAULT 0;
IF COL_LENGTH('dbo.Payments','RefundedAmount') IS NULL ALTER TABLE dbo.Payments ADD RefundedAmount int NOT NULL CONSTRAINT DF_Payments_RefundedAmount DEFAULT 0;
GO
IF OBJECT_ID('dbo.Refunds','U') IS NULL CREATE TABLE dbo.Refunds(
 Id uniqueidentifier NOT NULL DEFAULT NEWID() PRIMARY KEY,OrderId uniqueidentifier NOT NULL REFERENCES dbo.Orders(Id),PaymentId uniqueidentifier NULL REFERENCES dbo.Payments(Id),Amount int NOT NULL CHECK(Amount>0),Reason nvarchar(300) NOT NULL,Method nvarchar(20) NOT NULL,ProcessedBy uniqueidentifier NULL REFERENCES dbo.Users(Id),CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME());
IF OBJECT_ID('dbo.CashMovements','U') IS NULL CREATE TABLE dbo.CashMovements(
 Id uniqueidentifier NOT NULL DEFAULT NEWID() PRIMARY KEY,ShiftId uniqueidentifier NOT NULL REFERENCES dbo.WorkShifts(Id),MovementType nvarchar(20) NOT NULL CHECK(MovementType IN ('cash_in','cash_out','expense')),Amount int NOT NULL CHECK(Amount>0),Category nvarchar(100) NULL,Reason nvarchar(300) NOT NULL,CreatedBy uniqueidentifier NULL REFERENCES dbo.Users(Id),CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME());
GO
IF OBJECT_ID('dbo.ProductVariants','U') IS NULL CREATE TABLE dbo.ProductVariants(
 Id uniqueidentifier NOT NULL DEFAULT NEWID() PRIMARY KEY,ProductId uniqueidentifier NOT NULL REFERENCES dbo.Products(Id),Name nvarchar(80) NOT NULL,PriceDelta int NOT NULL DEFAULT 0,IsActive bit NOT NULL DEFAULT 1,Position int NOT NULL DEFAULT 0,CONSTRAINT UQ_ProductVariant UNIQUE(ProductId,Name));
IF OBJECT_ID('dbo.Combos','U') IS NULL CREATE TABLE dbo.Combos(
 Id uniqueidentifier NOT NULL DEFAULT NEWID() PRIMARY KEY,StoreId uniqueidentifier NOT NULL REFERENCES dbo.Stores(Id),Name nvarchar(150) NOT NULL,Price int NOT NULL CHECK(Price>=0),IsActive bit NOT NULL DEFAULT 1,StartsAt datetime2 NULL,EndsAt datetime2 NULL,CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME());
IF OBJECT_ID('dbo.ComboItems','U') IS NULL CREATE TABLE dbo.ComboItems(
 Id uniqueidentifier NOT NULL DEFAULT NEWID() PRIMARY KEY,ComboId uniqueidentifier NOT NULL REFERENCES dbo.Combos(Id),ProductId uniqueidentifier NOT NULL REFERENCES dbo.Products(Id),Quantity smallint NOT NULL DEFAULT 1 CHECK(Quantity>0));
GO
IF COL_LENGTH('dbo.Promotions','UsageLimit') IS NULL ALTER TABLE dbo.Promotions ADD UsageLimit int NULL;
IF COL_LENGTH('dbo.Promotions','PerCustomerLimit') IS NULL ALTER TABLE dbo.Promotions ADD PerCustomerLimit int NULL;
IF OBJECT_ID('dbo.PromotionUsages','U') IS NULL CREATE TABLE dbo.PromotionUsages(
 Id uniqueidentifier NOT NULL DEFAULT NEWID() PRIMARY KEY,PromotionId uniqueidentifier NOT NULL REFERENCES dbo.Promotions(Id),OrderId uniqueidentifier NOT NULL REFERENCES dbo.Orders(Id),CustomerId uniqueidentifier NULL REFERENCES dbo.Customers(Id),UsedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),CONSTRAINT UQ_PromotionUsageOrder UNIQUE(OrderId));
GO
IF OBJECT_ID('dbo.Reservations','U') IS NULL CREATE TABLE dbo.Reservations(
 Id uniqueidentifier NOT NULL DEFAULT NEWID() PRIMARY KEY,StoreId uniqueidentifier NOT NULL REFERENCES dbo.Stores(Id),TableId uniqueidentifier NULL REFERENCES dbo.DiningTables(Id),CustomerName nvarchar(150) NOT NULL,Phone nvarchar(30) NOT NULL,GuestCount smallint NOT NULL CHECK(GuestCount>0),ReservedAt datetime2 NOT NULL,Status nvarchar(20) NOT NULL DEFAULT N'booked',Note nvarchar(400) NULL,CreatedBy uniqueidentifier NULL REFERENCES dbo.Users(Id),CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME());
IF OBJECT_ID('dbo.Waitlist','U') IS NULL CREATE TABLE dbo.Waitlist(
 Id uniqueidentifier NOT NULL DEFAULT NEWID() PRIMARY KEY,StoreId uniqueidentifier NOT NULL REFERENCES dbo.Stores(Id),CustomerName nvarchar(150) NOT NULL,Phone nvarchar(30) NULL,GuestCount smallint NOT NULL,Status nvarchar(20) NOT NULL DEFAULT N'waiting',Note nvarchar(300) NULL,CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),SeatedAt datetime2 NULL);
GO
IF COL_LENGTH('dbo.Customers','Email') IS NULL ALTER TABLE dbo.Customers ADD Email nvarchar(200) NULL;
IF COL_LENGTH('dbo.Customers','Birthday') IS NULL ALTER TABLE dbo.Customers ADD Birthday date NULL;
IF COL_LENGTH('dbo.Customers','Tier') IS NULL ALTER TABLE dbo.Customers ADD Tier nvarchar(30) NOT NULL CONSTRAINT DF_Customers_Tier DEFAULT N'Member';
IF OBJECT_ID('dbo.CustomerPointTransactions','U') IS NULL CREATE TABLE dbo.CustomerPointTransactions(
 Id uniqueidentifier NOT NULL DEFAULT NEWID() PRIMARY KEY,CustomerId uniqueidentifier NOT NULL REFERENCES dbo.Customers(Id),OrderId uniqueidentifier NULL REFERENCES dbo.Orders(Id),Points int NOT NULL,Reason nvarchar(200) NOT NULL,ExpiresAt datetime2 NULL,CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME());
GO
IF OBJECT_ID('dbo.PurchaseOrders','U') IS NULL CREATE TABLE dbo.PurchaseOrders(
 Id uniqueidentifier NOT NULL DEFAULT NEWID() PRIMARY KEY,StoreId uniqueidentifier NOT NULL REFERENCES dbo.Stores(Id),SupplierId uniqueidentifier NULL REFERENCES dbo.Suppliers(Id),OrderNumber bigint IDENTITY(1000,1) NOT NULL UNIQUE,Status nvarchar(20) NOT NULL DEFAULT N'draft',ExpectedAt datetime2 NULL,TotalCost decimal(18,2) NOT NULL DEFAULT 0,Note nvarchar(400) NULL,CreatedBy uniqueidentifier NULL REFERENCES dbo.Users(Id),CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME());
IF OBJECT_ID('dbo.PurchaseOrderItems','U') IS NULL CREATE TABLE dbo.PurchaseOrderItems(
 Id uniqueidentifier NOT NULL DEFAULT NEWID() PRIMARY KEY,PurchaseOrderId uniqueidentifier NOT NULL REFERENCES dbo.PurchaseOrders(Id),IngredientId uniqueidentifier NOT NULL REFERENCES dbo.Ingredients(Id),Quantity decimal(18,3) NOT NULL,UnitCost decimal(18,4) NOT NULL);
IF OBJECT_ID('dbo.IngredientLots','U') IS NULL CREATE TABLE dbo.IngredientLots(
 Id uniqueidentifier NOT NULL DEFAULT NEWID() PRIMARY KEY,IngredientId uniqueidentifier NOT NULL REFERENCES dbo.Ingredients(Id),LotCode nvarchar(80) NULL,Quantity decimal(18,3) NOT NULL,ExpiresAt date NULL,ReceivedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),StockReceiptId uniqueidentifier NULL REFERENCES dbo.StockReceipts(Id));
IF OBJECT_ID('dbo.SupplierPayments','U') IS NULL CREATE TABLE dbo.SupplierPayments(
 Id uniqueidentifier NOT NULL DEFAULT NEWID() PRIMARY KEY,SupplierId uniqueidentifier NOT NULL REFERENCES dbo.Suppliers(Id),PurchaseOrderId uniqueidentifier NULL REFERENCES dbo.PurchaseOrders(Id),Amount decimal(18,2) NOT NULL,Method nvarchar(30) NOT NULL,Note nvarchar(300) NULL,PaidAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),CreatedBy uniqueidentifier NULL REFERENCES dbo.Users(Id));
GO
IF OBJECT_ID('dbo.WorkSchedules','U') IS NULL CREATE TABLE dbo.WorkSchedules(
 Id uniqueidentifier NOT NULL DEFAULT NEWID() PRIMARY KEY,UserId uniqueidentifier NOT NULL REFERENCES dbo.Users(Id),StoreId uniqueidentifier NOT NULL REFERENCES dbo.Stores(Id),StartsAt datetime2 NOT NULL,EndsAt datetime2 NOT NULL,HourlyRate decimal(18,2) NOT NULL DEFAULT 0,Note nvarchar(300) NULL,Status nvarchar(20) NOT NULL DEFAULT N'scheduled');
IF OBJECT_ID('dbo.AttendanceEntries','U') IS NULL CREATE TABLE dbo.AttendanceEntries(
 Id uniqueidentifier NOT NULL DEFAULT NEWID() PRIMARY KEY,UserId uniqueidentifier NOT NULL REFERENCES dbo.Users(Id),StoreId uniqueidentifier NOT NULL REFERENCES dbo.Stores(Id),CheckInAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),CheckOutAt datetime2 NULL,Note nvarchar(300) NULL);
GO
IF OBJECT_ID('dbo.UserPermissionOverrides','U') IS NULL CREATE TABLE dbo.UserPermissionOverrides(
 UserId uniqueidentifier NOT NULL REFERENCES dbo.Users(Id),PermissionKey nvarchar(80) NOT NULL,IsAllowed bit NOT NULL,PRIMARY KEY(UserId,PermissionKey));
IF OBJECT_ID('dbo.ElectronicInvoices','U') IS NULL CREATE TABLE dbo.ElectronicInvoices(
 Id uniqueidentifier NOT NULL DEFAULT NEWID() PRIMARY KEY,OrderId uniqueidentifier NOT NULL REFERENCES dbo.Orders(Id),InvoiceNumber nvarchar(80) NULL,BuyerName nvarchar(200) NULL,BuyerTaxCode nvarchar(30) NULL,BuyerEmail nvarchar(200) NULL,Subtotal int NOT NULL,TaxAmount int NOT NULL,Total int NOT NULL,Status nvarchar(20) NOT NULL DEFAULT N'draft',IssuedAt datetime2 NULL,CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),CONSTRAINT UQ_EInvoiceOrder UNIQUE(OrderId));
GO
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name='IX_Reservations_ReservedAt') CREATE INDEX IX_Reservations_ReservedAt ON dbo.Reservations(ReservedAt,Status);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name='IX_CashMovements_Shift') CREATE INDEX IX_CashMovements_Shift ON dbo.CashMovements(ShiftId,CreatedAt);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name='IX_Attendance_UserTime') CREATE INDEX IX_Attendance_UserTime ON dbo.AttendanceEntries(UserId,CheckInAt);
