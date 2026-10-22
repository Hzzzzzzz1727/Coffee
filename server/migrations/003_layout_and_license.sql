IF COL_LENGTH('dbo.Areas','CanvasWidth') IS NULL ALTER TABLE dbo.Areas ADD CanvasWidth int NOT NULL CONSTRAINT DF_Areas_CanvasWidth DEFAULT 1000;
IF COL_LENGTH('dbo.Areas','CanvasHeight') IS NULL ALTER TABLE dbo.Areas ADD CanvasHeight int NOT NULL CONSTRAINT DF_Areas_CanvasHeight DEFAULT 600;
IF COL_LENGTH('dbo.DiningTables','Width') IS NULL ALTER TABLE dbo.DiningTables ADD Width int NOT NULL CONSTRAINT DF_DiningTables_Width DEFAULT 190;
IF COL_LENGTH('dbo.DiningTables','Height') IS NULL ALTER TABLE dbo.DiningTables ADD Height int NOT NULL CONSTRAINT DF_DiningTables_Height DEFAULT 120;
IF COL_LENGTH('dbo.DiningTables','Shape') IS NULL ALTER TABLE dbo.DiningTables ADD Shape nvarchar(20) NOT NULL CONSTRAINT DF_DiningTables_Shape DEFAULT N'rectangle';
