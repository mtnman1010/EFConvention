-- =============================================================================
-- EFConvention — Version 2.2
-- Sql/StoreDb.sql
--
-- Creates the StoreDb database and all tables matching the v2.2 domain model.
--
-- Convention configuration applied (mirrors StoreDb.cs):
--   default PascalCase — all identifiers match C# class and property names
--   .WithFullAudit()   — IAuditable columns on Customer, Product, Order,
--                        ProductReview; ISoftDelete columns on Product and Order
--
-- Breaking changes from v2.1:
--   FK columns renamed — navigation property name, not TypeId:
--     address_id   → Address
--     customer_id  → Customer
--     category_id  → Category
--     order_id     → Order
--     product_id   → Product
--   Audit columns renamed:
--     created_at   → CreatedDate
--     created_by   → CreatedBy
--     modified_at  → ModifiedDate
--     modified_by  → ModifiedBy
--   Soft delete columns renamed:
--     deleted_at   → DeletedDate
--     deleted_by   → DeletedBy
--   All identifiers now PascalCase to match EF Core default convention
--
-- Entity → table mapping:
--   Address       → Address        (plain entity)
--   Category      → Category       (plain entity)
--   Customer      → Customer       (IAuditable, hard delete)
--   Product       → Product        (IAuditable + ISoftDelete, [Precision] decimals)
--   Order         → Order          (IAuditable + ISoftDelete)
--   OrderItem     → OrderItem      (plain entity, required FKs)
--   ProductReview → ProductReview  (IAuditable, optional customer FK)
--
-- Creation order respects FK dependencies:
--   Address → Customer → Order → OrderItem
--   Category → Product → OrderItem
--   Product + Customer → ProductReview
--
-- Target: SQL Server 2019+ / Azure SQL
-- =============================================================================


-- -----------------------------------------------------------------------------
-- Database
-- -----------------------------------------------------------------------------

USE master;
GO

IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = N'StoreDb')
    CREATE DATABASE StoreDb;
GO

USE StoreDb;
GO


-- =============================================================================
-- TABLES
-- =============================================================================


-- -----------------------------------------------------------------------------
-- Address — plain entity, no audit, no soft delete
-- -----------------------------------------------------------------------------

IF OBJECT_ID(N'dbo.Address', N'U') IS NULL
CREATE TABLE dbo.Address
(
    Id         INT           NOT NULL IDENTITY(1,1),
    Street     NVARCHAR(255) NOT NULL DEFAULT '',
    City       NVARCHAR(100) NOT NULL DEFAULT '',
    State      NVARCHAR(100) NOT NULL DEFAULT '',
    PostalCode NVARCHAR(20)  NOT NULL DEFAULT '',

    CONSTRAINT PK_Address PRIMARY KEY (Id)
);
GO


-- -----------------------------------------------------------------------------
-- Category — plain entity
-- -----------------------------------------------------------------------------

IF OBJECT_ID(N'dbo.Category', N'U') IS NULL
CREATE TABLE dbo.Category
(
    Id          INT           NOT NULL IDENTITY(1,1),
    Name        NVARCHAR(100) NOT NULL DEFAULT '',
    Description NVARCHAR(500) NOT NULL DEFAULT '',

    CONSTRAINT PK_Category PRIMARY KEY (Id)
);
GO


-- -----------------------------------------------------------------------------
-- Customer — IAuditable, hard delete
--
-- Address FK column named after navigation property "Address" (not AddressId).
-- Address is INT (non-nullable) → required FK detected automatically.
-- -----------------------------------------------------------------------------

IF OBJECT_ID(N'dbo.Customer', N'U') IS NULL
CREATE TABLE dbo.Customer
(
    Id      INT           NOT NULL IDENTITY(1,1),
    Name    NVARCHAR(255) NOT NULL DEFAULT '',
    Email   NVARCHAR(255) NOT NULL DEFAULT '',
    Phone   NVARCHAR(50)  NOT NULL DEFAULT '',
    Address INT           NOT NULL,               -- FK → Address.Id (required)

    -- IAuditable — stamped automatically by AuditInterceptor
    CreatedDate  DATETIME2     NOT NULL DEFAULT SYSUTCDATETIME(),
    CreatedBy    NVARCHAR(255) NOT NULL DEFAULT 'system',
    ModifiedDate DATETIME2     NULL,
    ModifiedBy   NVARCHAR(255) NULL,

    CONSTRAINT PK_Customer PRIMARY KEY (Id),
    CONSTRAINT FK_Customer_Address
        FOREIGN KEY (Address) REFERENCES dbo.Address (Id)
);
GO


-- -----------------------------------------------------------------------------
-- Product — IAuditable + ISoftDelete + DECIMAL(18,2)
--
-- Category FK column named after navigation property "Category" (not CategoryId).
-- Price and CostPrice use DECIMAL(18,2) matching [Precision(18,2)] on the
-- C# properties — applied automatically by ConfigureDecimalPrecision.
-- IsDeleted is NOT NULL DEFAULT 0; partial index mirrors the EF filter.
-- -----------------------------------------------------------------------------

IF OBJECT_ID(N'dbo.Product', N'U') IS NULL
CREATE TABLE dbo.Product
(
    Id          INT            NOT NULL IDENTITY(1,1),
    Name        NVARCHAR(255)  NOT NULL DEFAULT '',
    Description NVARCHAR(1000) NOT NULL DEFAULT '',
    Sku         NVARCHAR(100)  NOT NULL DEFAULT '',
    Price       DECIMAL(18,2)  NOT NULL DEFAULT 0,   -- [Precision(18,2)]
    CostPrice   DECIMAL(18,2)  NOT NULL DEFAULT 0,   -- [Precision(18,2)]
    Category    INT            NOT NULL,              -- FK → Category.Id (required)

    -- IAuditable
    CreatedDate  DATETIME2     NOT NULL DEFAULT SYSUTCDATETIME(),
    CreatedBy    NVARCHAR(255) NOT NULL DEFAULT 'system',
    ModifiedDate DATETIME2     NULL,
    ModifiedBy   NVARCHAR(255) NULL,

    -- ISoftDelete — row never physically removed
    IsDeleted   BIT           NOT NULL DEFAULT 0,
    DeletedDate DATETIME2     NULL,
    DeletedBy   NVARCHAR(255) NULL,

    CONSTRAINT PK_Product PRIMARY KEY (Id),
    CONSTRAINT FK_Product_Category
        FOREIGN KEY (Category) REFERENCES dbo.Category (Id)
);
GO


-- -----------------------------------------------------------------------------
-- Order — IAuditable + ISoftDelete + DECIMAL(18,2)
--
-- Customer FK column named after navigation property "Customer" (not CustomerId).
-- Customer is INT (non-nullable) AND has [Required] on the navigation property
-- → required FK detected by both the attribute check and non-nullable FK type.
-- "Order" is a SQL Server reserved word — bracket-quote in raw SQL.
-- -----------------------------------------------------------------------------

IF OBJECT_ID(N'dbo.[Order]', N'U') IS NULL
CREATE TABLE dbo.[Order]
(
    Id          INT           NOT NULL IDENTITY(1,1),
    OrderDate   DATETIME2     NOT NULL,
    Status      NVARCHAR(50)  NOT NULL DEFAULT 'Pending',
    TotalAmount DECIMAL(18,2) NOT NULL DEFAULT 0,    -- [Precision(18,2)]
    Customer    INT           NOT NULL,               -- FK → Customer.Id (required)

    -- IAuditable
    CreatedDate  DATETIME2     NOT NULL DEFAULT SYSUTCDATETIME(),
    CreatedBy    NVARCHAR(255) NOT NULL DEFAULT 'system',
    ModifiedDate DATETIME2     NULL,
    ModifiedBy   NVARCHAR(255) NULL,

    -- ISoftDelete
    IsDeleted   BIT           NOT NULL DEFAULT 0,
    DeletedDate DATETIME2     NULL,
    DeletedBy   NVARCHAR(255) NULL,

    CONSTRAINT PK_Order PRIMARY KEY (Id),
    CONSTRAINT FK_Order_Customer
        FOREIGN KEY (Customer) REFERENCES dbo.Customer (Id)
);
GO


-- -----------------------------------------------------------------------------
-- OrderItem — plain entity, required FKs to both Order and Product
--
-- Order and Product FK columns named after navigation properties.
-- Both are INT (non-nullable) → both detected as required FKs.
-- UnitPrice uses DECIMAL(18,2) matching [Precision(18,2)].
-- -----------------------------------------------------------------------------

IF OBJECT_ID(N'dbo.OrderItem', N'U') IS NULL
CREATE TABLE dbo.OrderItem
(
    Id        INT           NOT NULL IDENTITY(1,1),
    Quantity  INT           NOT NULL DEFAULT 1,
    UnitPrice DECIMAL(18,2) NOT NULL DEFAULT 0,  -- [Precision(18,2)]
    Order     INT           NOT NULL,             -- FK → Order.Id (required)
    Product   INT           NOT NULL,             -- FK → Product.Id (required)

    CONSTRAINT PK_OrderItem PRIMARY KEY (Id),
    CONSTRAINT FK_OrderItem_Order
        FOREIGN KEY ([Order])  REFERENCES dbo.[Order] (Id),
    CONSTRAINT FK_OrderItem_Product
        FOREIGN KEY (Product) REFERENCES dbo.Product (Id)
);
GO


-- -----------------------------------------------------------------------------
-- ProductReview — IAuditable, optional Customer FK
--
-- Customer FK column is INT NULL (nullable) → optional FK detected automatically.
-- Product FK column is INT NOT NULL → required FK.
-- Reviews survive customer account deletion — no ON DELETE CASCADE.
-- -----------------------------------------------------------------------------

IF OBJECT_ID(N'dbo.ProductReview', N'U') IS NULL
CREATE TABLE dbo.ProductReview
(
    Id       INT           NOT NULL IDENTITY(1,1),
    Rating   INT           NOT NULL DEFAULT 1,   -- 1–5
    Comment  NVARCHAR(MAX) NOT NULL DEFAULT '',
    Product  INT           NOT NULL,             -- FK → Product.Id (required)
    Customer INT           NULL,                 -- FK → Customer.Id (optional)

    -- IAuditable
    CreatedDate  DATETIME2     NOT NULL DEFAULT SYSUTCDATETIME(),
    CreatedBy    NVARCHAR(255) NOT NULL DEFAULT 'system',
    ModifiedDate DATETIME2     NULL,
    ModifiedBy   NVARCHAR(255) NULL,

    CONSTRAINT PK_ProductReview PRIMARY KEY (Id),
    CONSTRAINT FK_ProductReview_Product
        FOREIGN KEY (Product)  REFERENCES dbo.Product  (Id),
    CONSTRAINT FK_ProductReview_Customer
        FOREIGN KEY (Customer) REFERENCES dbo.Customer (Id)
        -- No ON DELETE CASCADE — optional relationship, review survives customer deletion
);
GO


-- =============================================================================
-- INDEXES
-- =============================================================================

-- Customer → Address
IF NOT EXISTS (SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.Customer') AND name = N'IX_Customer_Address')
    CREATE INDEX IX_Customer_Address ON dbo.Customer (Address);
GO

-- Order → Customer (active rows only — mirrors ISoftDelete global filter)
IF NOT EXISTS (SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.[Order]') AND name = N'IX_Order_Active')
    CREATE INDEX IX_Order_Active
        ON dbo.[Order] (Customer, OrderDate)
        WHERE IsDeleted = 0;
GO

-- Order → Customer (all rows — for deleted order queries via IgnoreQueryFilters)
IF NOT EXISTS (SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.[Order]') AND name = N'IX_Order_Customer')
    CREATE INDEX IX_Order_Customer ON dbo.[Order] (Customer);
GO

-- Product (active rows only — mirrors ISoftDelete global filter)
IF NOT EXISTS (SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.Product') AND name = N'IX_Product_Active')
    CREATE INDEX IX_Product_Active
        ON dbo.Product (Category, Name)
        WHERE IsDeleted = 0;
GO

-- OrderItem → Order
IF NOT EXISTS (SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.OrderItem') AND name = N'IX_OrderItem_Order')
    CREATE INDEX IX_OrderItem_Order ON dbo.OrderItem ([Order]);
GO

-- OrderItem → Product
IF NOT EXISTS (SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.OrderItem') AND name = N'IX_OrderItem_Product')
    CREATE INDEX IX_OrderItem_Product ON dbo.OrderItem (Product);
GO

-- ProductReview → Product
IF NOT EXISTS (SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.ProductReview') AND name = N'IX_ProductReview_Product')
    CREATE INDEX IX_ProductReview_Product ON dbo.ProductReview (Product);
GO

-- ProductReview → Customer (nullable — supports optional FK queries)
IF NOT EXISTS (SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.ProductReview') AND name = N'IX_ProductReview_Customer')
    CREATE INDEX IX_ProductReview_Customer ON dbo.ProductReview (Customer)
        WHERE Customer IS NOT NULL;
GO


-- =============================================================================
-- VERIFICATION
-- =============================================================================

SELECT
    t.name          AS TableName,
    SUM(p.rows)     AS RowCount,
    CASE
        WHEN t.name IN ('Product','Order') THEN 'ISoftDelete + IAuditable'
        WHEN t.name IN ('Customer','ProductReview') THEN 'IAuditable'
        ELSE 'plain entity'
    END             AS EntityType
FROM sys.tables     t
JOIN sys.partitions p
    ON  p.object_id = t.object_id
    AND p.index_id  IN (0, 1)
WHERE t.schema_id = SCHEMA_ID('dbo')
  AND t.name IN ('Address','Category','Customer','Product',
                 'Order','OrderItem','ProductReview')
GROUP BY t.name
ORDER BY t.name;
GO