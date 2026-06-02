# Naming Conventions

EFConventionBuilder applies a single naming convention to all table names, column names, and FK column names across every entity in your domain. The convention is set once on the builder and applied uniformly — no per-entity configuration required.

---

## Built-in conventions

Three strategies ship with the library:

| Method | Table | Column | FK column |
|---|---|---|---|
| *(default — PascalCase)* | `Customer` | `OrderDate` | `Customer` |
| `UseSnakeCase()` | `customer` | `order_date` | `customer` |
| `UsePluralizedTables()` | `Customers` | `OrderDate` | `Customer` |
| `UseNamingConvention(custom)` | your choice | your choice | your choice |

---

## PascalCase (default)

Activated by not calling any convention method, or explicitly:

```csharp
EntityConventionBuilder.ForAssemblyOf<Customer>()
    .UseNamingConvention(new PascalCaseNamingConvention())
    .WithFullAudit()
```

Names match C# class and property names exactly — no transformation applied. This is the EF Core default and the most common convention for SQL Server projects.

**Table names:**
```
class Customer      → table Customer
class OrderItem     → table OrderItem
class ProductReview → table ProductReview
```

**Column names:**
```
property OrderDate  → column OrderDate
property TotalAmount → column TotalAmount
property IsDeleted  → column IsDeleted
```

**FK columns:**
```
navigation Customer on Order → column Customer
navigation Address on Customer → column Address
navigation Payee on Withholding → column Payee
```

**Audit columns (defaults):**
```
CreatedDate, CreatedBy, ModifiedDate, ModifiedBy
```

**Soft delete columns (defaults):**
```
IsDeleted, DeletedDate, DeletedBy
```

---

## snake_case

Activated by `.UseSnakeCase()`. Recommended for PostgreSQL, which treats unquoted identifiers as lowercase.

```csharp
EntityConventionBuilder.ForAssemblyOf<Customer>()
    .UseSnakeCase()
    .WithFullAudit()
```

Every PascalCase identifier is converted by inserting an underscore before each uppercase letter that follows a lowercase letter or digit, then lowercasing the entire string.

**Rule:** `(?<=[a-z0-9])([A-Z])` → `_$1`, then `.ToLower()`

**Table names:**
```
class Customer      → table customer
class OrderItem     → table order_item
class ProductReview → table product_review
```

**Column names:**
```
property OrderDate   → column order_date
property TotalAmount → column total_amount
property IsDeleted   → column is_deleted
```

**FK columns:**
```
navigation Customer on Order       → column customer
navigation Address on Customer     → column address
navigation Payee on Withholding    → column payee
navigation OnBehalfOf on Withholding → column on_behalf_of
```

**Audit columns:**
```
created_date, created_by, modified_date, modified_by
```

**Soft delete columns:**
```
is_deleted, deleted_date, deleted_by
```

---

## Pluralized tables

Activated by `.UsePluralizedTables()`. Table names are pluralized using basic English rules. Column and FK names remain in PascalCase — only the table name changes.

```csharp
EntityConventionBuilder.ForAssemblyOf<Customer>()
    .UsePluralizedTables()
    .WithFullAudit()
```

**Pluralization rules:**

| Ending | Rule | Example |
|---|---|---|
| `y` | replace with `ies` | `Category` → `Categories` |
| `s` | append `es` | `Address` → `Addresses` |
| anything else | append `s` | `Customer` → `Customers` |

**Table names:**
```
class Customer      → table Customers
class Category      → table Categories
class Address       → table Addresses
class OrderItem     → table OrderItems
class ProductReview → table ProductReviews
```

**Column and FK names** — same as PascalCase (unchanged):
```
property OrderDate → column OrderDate
navigation Customer on Order → column Customer
```

**Advanced pluralisation:** The built-in strategy covers basic English rules. For irregular nouns or non-English domains, implement `IEntityNamingConvention` with [Humanizer](https://github.com/Humanizr/Humanizer):

```csharp
public class HumanizerNamingConvention : IEntityNamingConvention
{
    public string GetTableName(Type entityType) =>
        entityType.Name.Pluralize();  // Humanizer handles irregular nouns
    public string GetColumnName(PropertyInfo property) => property.Name;
    public string GetForeignKeyName(PropertyInfo nav, Type principal) => nav.Name;
    public string ApplyToName(string logicalName) => logicalName;
}
```

---

## FK column naming

FK columns are named after the **navigation property name** on the dependent entity — not the principal type name with an `Id` suffix. This is more expressive and handles multiple navigations to the same type cleanly.

```csharp
// Three navigations to Person — all FK columns named after the navigation
public class Withholding : IEntity
{
    public int    Id         { get; set; }
    public Person Person     { get; set; } = null!;  // FK column: Person
    public Person Payee      { get; set; } = null!;  // FK column: Payee
    public Person? OnBehalfOf { get; set; }           // FK column: OnBehalfOf
}
```

Under PascalCase: `Person`, `Payee`, `OnBehalfOf`
Under snake_case: `person`, `payee`, `on_behalf_of`

EF Core creates a shadow property named `PersonId`, `PayeeId`, `OnBehalfOfId` internally — the library renames only the database column. Scalar FK properties on the domain object are optional.

---

## Custom naming convention

Implement `IEntityNamingConvention` for full control over all identifiers:

```csharp
public interface IEntityNamingConvention
{
    string GetTableName(Type entityType);
    string GetColumnName(PropertyInfo property);
    string GetForeignKeyName(PropertyInfo nav, Type principal);
    string ApplyToName(string logicalName);
}
```

`ApplyToName` is used by the builder to apply the convention to audit and soft-delete column name overrides. If you override audit column names the convention is applied on top:

```csharp
.WithAuditFields(cols => cols.CreatedDate = "RecordCreatedDate")
// Under snake_case: "RecordCreatedDate" → "record_created_date"
// Under PascalCase: "RecordCreatedDate" → "RecordCreatedDate" (unchanged)
```

### Example — prefixed tables

```csharp
public class PrefixedNamingConvention : IEntityNamingConvention
{
    private readonly string _prefix;
    public PrefixedNamingConvention(string prefix) => _prefix = prefix;

    public string GetTableName(Type entityType)                       => $"{_prefix}{entityType.Name}";
    public string GetColumnName(PropertyInfo property)                => property.Name;
    public string GetForeignKeyName(PropertyInfo nav, Type principal) => nav.Name;
    public string ApplyToName(string logicalName)                     => logicalName;
}

// Usage — tbl_Customer, tbl_Order, tbl_Product
EntityConventionBuilder
    .ForAssemblyOf<Customer>()
    .UseNamingConvention(new PrefixedNamingConvention("tbl_"))
    .WithFullAudit();
```

### Example — schema-prefixed tables

```csharp
public class SchemaPrefixedConvention : IEntityNamingConvention
{
    private readonly string _schema;
    public SchemaPrefixedConvention(string schema) => _schema = schema;

    public string GetTableName(Type entityType)                       => $"{_schema}.{entityType.Name}";
    public string GetColumnName(PropertyInfo property)                => property.Name;
    public string GetForeignKeyName(PropertyInfo nav, Type principal) => nav.Name;
    public string ApplyToName(string logicalName)                     => logicalName;
}
```

---

## Overriding audit and soft-delete column names

The active naming convention is always applied on top of any override. This means overrides are logical names — the convention transforms them for the database.

```csharp
// Legacy schema with different audit column names
EntityConventionBuilder
    .ForAssemblyOf<Customer>()
    .UseSnakeCase()
    .WithAuditFields(cols =>
    {
        cols.CreatedDate  = "RecordCreatedDate";   // → record_created_date
        cols.CreatedBy    = "RecordCreatedUser";   // → record_created_user
        cols.ModifiedDate = "RecordModifiedDate";  // → record_modified_date
        cols.ModifiedBy   = "RecordModifiedUser";  // → record_modified_user
    })
    .WithSoftDelete(cols =>
    {
        cols.IsDeleted   = "Archived";    // → archived
        cols.DeletedDate = "ArchivedDate"; // → archived_date
        cols.DeletedBy   = "ArchivedBy";  // → archived_by
    });
```

---

## Reserved SQL words

Some entity names produce table names that are SQL Server reserved keywords. EF Core handles quoting automatically in generated queries, but hand-written SQL must bracket-quote them:

| Entity | PascalCase table | snake_case table | Raw SQL |
|---|---|---|---|
| `Order` | `Order` | `order` | `dbo.[Order]` or `dbo.[order]` |

---

## Complete reference — all three conventions

The table below shows how the full Store domain maps under each convention.

### Tables

| C# class | PascalCase | snake_case | Pluralized |
|---|---|---|---|
| `Address` | `Address` | `address` | `Addresses` |
| `Category` | `Category` | `category` | `Categories` |
| `Customer` | `Customer` | `customer` | `Customers` |
| `Product` | `Product` | `product` | `Products` |
| `Order` | `Order` | `order` | `Orders` |
| `OrderItem` | `OrderItem` | `order_item` | `OrderItems` |
| `ProductReview` | `ProductReview` | `product_review` | `ProductReviews` |

### FK columns

| Navigation | PascalCase | snake_case |
|---|---|---|
| `Customer.Address` | `Address` | `address` |
| `Order.Customer` | `Customer` | `customer` |
| `OrderItem.Order` | `Order` | `order` |
| `OrderItem.Product` | `Product` | `product` |
| `ProductReview.Product` | `Product` | `product` |
| `ProductReview.Customer` | `Customer` | `customer` |

### Audit columns

| Property | PascalCase | snake_case |
|---|---|---|
| `CreatedDate` | `CreatedDate` | `created_date` |
| `CreatedBy` | `CreatedBy` | `created_by` |
| `ModifiedDate` | `ModifiedDate` | `modified_date` |
| `ModifiedBy` | `ModifiedBy` | `modified_by` |

### Soft delete columns

| Property | PascalCase | snake_case |
|---|---|---|
| `IsDeleted` | `IsDeleted` | `is_deleted` |
| `DeletedDate` | `DeletedDate` | `deleted_date` |
| `DeletedBy` | `DeletedBy` | `deleted_by` |

---

## Next steps

- [[Relationships]] — how FK relationships are wired and how required/optional is detected
- [[Audit Stamping]] — audit column configuration and `ICurrentUserService`
- [[Soft Delete]] — soft delete column configuration and the global query filter
