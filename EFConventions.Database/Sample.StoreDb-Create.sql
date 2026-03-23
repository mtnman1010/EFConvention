-- =============================================================================
-- EFConventions — Version 2.1
-- Sql/StoreDb.sql
--
-- Creates the StoreDb database and all tables matching the v2 domain model.
--
-- Convention configuration applied (mirrors StoreDb.cs):
--   .UseSnakeCase()   — all identifiers are snake_case
--   .WithFullAudit()  — IAuditable columns on Customer, Product, Order, ProductReview
--                       ISoftDelete columns on Product and Order
--
-- Entity → table mapping:
--   Address       → address        (plain entity)
--   Category      → category       (plain entity)
--   Customer      → customer       (IAuditable, hard delete)
--   Product       → product        (IAuditable + ISoftDelete, [Precision] decimals)
--   Order         → order          (IAuditable + ISoftDelete)
--   OrderItem     → order_item     (plain entity, required FKs)
--   ProductReview → product_review (IAuditable, optional customer FK)
--
-- Creation order respects FK dependencies:
--   address → customer → order → order_item
--   category → product → order_item
--   product + customer → product_review
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
-- address — plain entity, no audit, no soft delete
-- -----------------------------------------------------------------------------

IF OBJECT_ID(N'dbo.address', N'U') IS NULL
CREATE TABLE dbo.address
(
    id          INT           NOT NULL IDENTITY(1,1),
    street      NVARCHAR(255) NOT NULL DEFAULT '',
    city        NVARCHAR(100) NOT NULL DEFAULT '',
    state       NVARCHAR(100) NOT NULL DEFAULT '',
    postal_code NVARCHAR(20)  NOT NULL DEFAULT '',

    CONSTRAINT PK_address PRIMARY KEY (id)
);
GO


-- -----------------------------------------------------------------------------
-- category — plain entity
-- (demonstrates y → ies pluralisation: Category → Categories under pluralised
--  convention; StoreDb uses snake_case so the table name is category)
-- -----------------------------------------------------------------------------

IF OBJECT_ID(N'dbo.category', N'U') IS NULL
CREATE TABLE dbo.category
(
    id          INT           NOT NULL IDENTITY(1,1),
    name        NVARCHAR(100) NOT NULL DEFAULT '',
    description NVARCHAR(500) NOT NULL DEFAULT '',

    CONSTRAINT PK_category PRIMARY KEY (id)
);
GO


-- -----------------------------------------------------------------------------
-- customer — IAuditable, hard delete
--
-- address_id is INT (non-nullable) → required FK, detected automatically
-- by IsRequiredNavigation via the non-nullable FK type check.
-- -----------------------------------------------------------------------------

IF OBJECT_ID(N'dbo.customer', N'U') IS NULL
CREATE TABLE dbo.customer
(
    id         INT           NOT NULL IDENTITY(1,1),
    name       NVARCHAR(255) NOT NULL DEFAULT '',
    email      NVARCHAR(255) NOT NULL DEFAULT '',
    phone      NVARCHAR(50)  NOT NULL DEFAULT '',
    address_id INT           NOT NULL,

    -- IAuditable — stamped automatically by UnitOfWork.SaveChanges
    created_at  DATETIME2     NOT NULL DEFAULT SYSUTCDATETIME(),
    created_by  NVARCHAR(255) NOT NULL DEFAULT 'system',
    modified_at DATETIME2     NULL,
    modified_by NVARCHAR(255) NULL,

    CONSTRAINT PK_customer PRIMARY KEY (id),
    CONSTRAINT FK_customer_address
        FOREIGN KEY (address_id) REFERENCES dbo.address (id)
);
GO


-- -----------------------------------------------------------------------------
-- product — IAuditable + ISoftDelete + DECIMAL(18,2) from [Precision(18,2)]
--
-- category_id is INT (non-nullable) → required FK.
-- price and cost_price use DECIMAL(18,2) matching [Precision(18,2)] on the
-- C# properties — applied automatically by ConfigureDecimalPrecision.
-- is_deleted is NOT NULL DEFAULT 0; the partial index mirrors the EF filter.
-- -----------------------------------------------------------------------------

IF OBJECT_ID(N'dbo.product', N'U') IS NULL
CREATE TABLE dbo.product
(
    id          INT            NOT NULL IDENTITY(1,1),
    name        NVARCHAR(255)  NOT NULL DEFAULT '',
    description NVARCHAR(1000) NOT NULL DEFAULT '',
    sku         NVARCHAR(100)  NOT NULL DEFAULT '',
    price       DECIMAL(18,2)  NOT NULL DEFAULT 0,     -- [Precision(18,2)]
    cost_price  DECIMAL(18,2)  NOT NULL DEFAULT 0,     -- [Precision(18,2)]
    category_id INT            NOT NULL,

    -- IAuditable
    created_at  DATETIME2     NOT NULL DEFAULT SYSUTCDATETIME(),
    created_by  NVARCHAR(255) NOT NULL DEFAULT 'system',
    modified_at DATETIME2     NULL,
    modified_by NVARCHAR(255) NULL,

    -- ISoftDelete — row never physically removed
    is_deleted BIT           NOT NULL DEFAULT 0,
    deleted_at DATETIME2     NULL,
    deleted_by NVARCHAR(255) NULL,

    CONSTRAINT PK_product PRIMARY KEY (id),
    CONSTRAINT FK_product_category
        FOREIGN KEY (category_id) REFERENCES dbo.category (id)
);
GO


-- -----------------------------------------------------------------------------
-- order — IAuditable + ISoftDelete + DECIMAL(18,2)
--
-- customer_id is INT (non-nullable) AND has [Required] on the navigation
-- property → required FK detected by both the attribute check and the
-- non-nullable FK type check (either alone is sufficient).
-- "order" is a SQL Server reserved word — bracket-quote in raw SQL.
-- -----------------------------------------------------------------------------

IF OBJECT_ID(N'dbo.[order]', N'U') IS NULL
CREATE TABLE dbo.[order]
(
    id           INT           NOT NULL IDENTITY(1,1),
    order_date   DATETIME2     NOT NULL,
    status       NVARCHAR(50)  NOT NULL DEFAULT 'Pending',
    total_amount DECIMAL(18,2) NOT NULL DEFAULT 0,     -- [Precision(18,2)]
    customer_id  INT           NOT NULL,               -- required FK

    -- IAuditable
    created_at  DATETIME2     NOT NULL DEFAULT SYSUTCDATETIME(),
    created_by  NVARCHAR(255) NOT NULL DEFAULT 'system',
    modified_at DATETIME2     NULL,
    modified_by NVARCHAR(255) NULL,

    -- ISoftDelete
    is_deleted BIT           NOT NULL DEFAULT 0,
    deleted_at DATETIME2     NULL,
    deleted_by NVARCHAR(255) NULL,

    CONSTRAINT PK_order PRIMARY KEY (id),
    CONSTRAINT FK_order_customer
        FOREIGN KEY (customer_id) REFERENCES dbo.customer (id)
);
GO


-- -----------------------------------------------------------------------------
-- order_item — plain entity, required FKs to both order and product
--
-- Both order_id and product_id are INT (non-nullable) → both detected as
-- required FKs. unit_price uses DECIMAL(18,2) matching [Precision(18,2)].
-- -----------------------------------------------------------------------------

IF OBJECT_ID(N'dbo.order_item', N'U') IS NULL
CREATE TABLE dbo.order_item
(
    id         INT           NOT NULL IDENTITY(1,1),
    quantity   INT           NOT NULL DEFAULT 1,
    unit_price DECIMAL(18,2) NOT NULL DEFAULT 0,   -- [Precision(18,2)]
    order_id   INT           NOT NULL,             -- required FK
    product_id INT           NOT NULL,             -- required FK

    CONSTRAINT PK_order_item PRIMARY KEY (id),
    CONSTRAINT FK_order_item_order
        FOREIGN KEY (order_id)   REFERENCES dbo.[order] (id),
    CONSTRAINT FK_order_item_product
        FOREIGN KEY (product_id) REFERENCES dbo.product (id)
);
GO


-- -----------------------------------------------------------------------------
-- product_review — IAuditable, optional customer_id FK
--
-- customer_id is INT? (nullable) → optional FK detected automatically by
-- IsRequiredNavigation: Nullable.GetUnderlyingType(int?) != null → optional.
-- This means reviews survive customer account deletion.
-- product_id is INT (non-nullable) → required FK.
-- -----------------------------------------------------------------------------

IF OBJECT_ID(N'dbo.product_review', N'U') IS NULL
CREATE TABLE dbo.product_review
(
    id          INT           NOT NULL IDENTITY(1,1),
    rating      INT           NOT NULL DEFAULT 1,    -- 1–5
    comment     NVARCHAR(MAX) NOT NULL DEFAULT '',
    product_id  INT           NOT NULL,              -- required FK
    customer_id INT           NULL,                  -- optional FK (int?)

    -- IAuditable
    created_at  DATETIME2     NOT NULL DEFAULT SYSUTCDATETIME(),
    created_by  NVARCHAR(255) NOT NULL DEFAULT 'system',
    modified_at DATETIME2     NULL,
    modified_by NVARCHAR(255) NULL,

    CONSTRAINT PK_product_review PRIMARY KEY (id),
    CONSTRAINT FK_product_review_product
        FOREIGN KEY (product_id)  REFERENCES dbo.product  (id),
    CONSTRAINT FK_product_review_customer
        FOREIGN KEY (customer_id) REFERENCES dbo.customer (id)
        -- No ON DELETE CASCADE — optional relationship, review survives customer deletion
);
GO


-- =============================================================================
-- INDEXES
-- =============================================================================

-- customer → address
IF NOT EXISTS (SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.customer') AND name = N'IX_customer_address_id')
    CREATE INDEX IX_customer_address_id ON dbo.customer (address_id);
GO

-- order → customer (active rows only — mirrors ISoftDelete global filter)
IF NOT EXISTS (SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.[order]') AND name = N'IX_order_active')
    CREATE INDEX IX_order_active
        ON dbo.[order] (customer_id, order_date)
        WHERE is_deleted = 0;
GO

-- order → customer (all rows — for deleted order queries via IgnoreQueryFilters)
IF NOT EXISTS (SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.[order]') AND name = N'IX_order_customer_id')
    CREATE INDEX IX_order_customer_id ON dbo.[order] (customer_id);
GO

-- product (active rows only — mirrors ISoftDelete global filter)
IF NOT EXISTS (SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.product') AND name = N'IX_product_active')
    CREATE INDEX IX_product_active
        ON dbo.product (category_id, name)
        WHERE is_deleted = 0;
GO

-- order_item → order
IF NOT EXISTS (SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.order_item') AND name = N'IX_order_item_order_id')
    CREATE INDEX IX_order_item_order_id ON dbo.order_item (order_id);
GO

-- order_item → product
IF NOT EXISTS (SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.order_item') AND name = N'IX_order_item_product_id')
    CREATE INDEX IX_order_item_product_id ON dbo.order_item (product_id);
GO

-- product_review → product
IF NOT EXISTS (SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.product_review') AND name = N'IX_product_review_product_id')
    CREATE INDEX IX_product_review_product_id ON dbo.product_review (product_id);
GO

-- product_review → customer (nullable — supports optional FK queries)
IF NOT EXISTS (SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.product_review') AND name = N'IX_product_review_customer_id')
    CREATE INDEX IX_product_review_customer_id ON dbo.product_review (customer_id)
        WHERE customer_id IS NOT NULL;
GO


-- =============================================================================
-- VERIFICATION
-- =============================================================================

SELECT
    t.name          AS table_name,
    SUM(p.rows)     AS row_count,
    CASE
        WHEN t.name IN ('product','order') THEN 'ISoftDelete + IAuditable'
        WHEN t.name IN ('customer','product_review') THEN 'IAuditable'
        ELSE 'plain entity'
    END             AS entity_type
FROM sys.tables     t
JOIN sys.partitions p
    ON  p.object_id = t.object_id
    AND p.index_id  IN (0, 1)
WHERE t.schema_id = SCHEMA_ID('dbo')
  AND t.name IN ('address','category','customer','product',
                 'order','order_item','product_review')
GROUP BY t.name
ORDER BY t.name;
GO
