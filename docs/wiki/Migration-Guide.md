# Migration Guide

This page covers every breaking change from v2.1 through v2.3 with before/after examples. If you are upgrading from v2.1 skip straight to the combined checklist at the bottom — it covers everything in one pass.

---

## v2.1 → v2.2

### 1. IAuditable property renames

The `At` suffix was replaced with `Date` throughout:

```csharp
// v2.1
public DateTime  CreatedAt  { get; set; }
public string    CreatedBy  { get; set; } = string.Empty;
public DateTime? ModifiedAt { get; set; }
public string?   ModifiedBy { get; set; }

// v2.2+
public DateTime  CreatedDate  { get; set; }
public string    CreatedBy    { get; set; } = string.Empty;
public DateTime? ModifiedDate { get; set; }
public string?   ModifiedBy   { get; set; }
```

### 2. ISoftDelete property rename

```csharp
// v2.1
public bool      IsDeleted { get; set; }
public DateTime? DeletedAt { get; set; }
public string?   DeletedBy { get; set; }

// v2.2+
public bool      IsDeleted   { get; set; }
public DateTime? DeletedDate { get; set; }
public string?   DeletedBy   { get; set; }
```

### 3. AuditColumnNames property renames

```csharp
// v2.1
.WithAuditFields(cols =>
{
    cols.CreatedAt  = "RecordCreatedDate";
    cols.CreatedBy  = "RecordCreatedUser";
    cols.ModifiedAt = "RecordModifiedDate";
    cols.ModifiedBy = "RecordModifiedUser";
})

// v2.2+
.WithAuditFields(cols =>
{
    cols.CreatedDate  = "RecordCreatedDate";
    cols.CreatedBy    = "RecordCreatedUser";
    cols.ModifiedDate = "RecordModifiedDate";
    cols.ModifiedBy   = "RecordModifiedUser";
})
```

### 4. SoftDeleteColumnNames property rename

```csharp
// v2.1
.WithSoftDelete(cols =>
{
    cols.DeletedAt = "ArchivedAt";
    cols.DeletedBy = "ArchivedBy";
})

// v2.2+
.WithSoftDelete(cols =>
{
    cols.DeletedDate = "ArchivedDate";
    cols.DeletedBy   = "ArchivedBy";
})
```

### 5. Default column names changed

If you relied on the default audit column names in your SQL schema or migrations, update them:

| Property | v2.1 default | v2.2+ default |
|---|---|---|
| `CreatedDate` | `CreatedAt` | `CreatedDate` |
| `ModifiedDate` | `ModifiedAt` | `ModifiedDate` |
| `DeletedDate` | `DeletedAt` | `DeletedDate` |

All other defaults (`CreatedBy`, `ModifiedBy`, `DeletedBy`, `IsDeleted`) are unchanged.

### 6. FK column naming changed

FK columns are now named after the navigation property, not the type with `Id` suffix:

```sql
-- v2.1 schema
customer_id INT NOT NULL   -- snake_case convention
CustomerId  INT NOT NULL   -- PascalCase convention

-- v2.2+ schema
customer    INT NOT NULL   -- snake_case convention
Customer    INT NOT NULL   -- PascalCase convention
```

Update any hand-written SQL scripts, stored procedures, or raw queries that reference FK column names by the old `TypeId` style.

### 7. AuditInterceptor stamping method names

If you reference `IAuditable` property names directly in code (e.g. in tests or custom stamping logic):

```csharp
// v2.1
entry.Entity.CreatedAt  = DateTime.UtcNow;
entry.Entity.ModifiedAt = DateTime.UtcNow;

// v2.2+
entry.Entity.CreatedDate  = DateTime.UtcNow;
entry.Entity.ModifiedDate = DateTime.UtcNow;
```

### 8. ServiceBase stamping

```csharp
// v2.1
softDeletable.DeletedAt = DateTime.UtcNow;

// v2.2+
softDeletable.DeletedDate = DateTime.UtcNow;
```

---

## v2.2 → v2.3

### 1. Scalar FK properties are now optional

Domain objects no longer need scalar FK properties (`CategoryId`, `CustomerId`, `AddressId` etc.). Required/optional detection uses nullable reference type annotations on the navigation property.

You can remove them for cleaner domain objects, or leave them in place — both work:

```csharp
// v2.2 — scalar FK property required for required/optional detection
public int      CustomerId { get; set; }
public Customer Customer   { get; set; } = null!;

// v2.3 — scalar FK optional, nullability drives detection
public Customer Customer { get; set; } = null!;  // non-nullable → required
public Customer? Customer { get; set; }           // nullable    → optional
```

**Requires `<Nullable>enable</Nullable>` in your `.csproj`.** Without it the compiler treats all reference types as nullable and the builder cannot distinguish required from optional — all become optional.

### 2. LINQ queries — navigate through navigation property

If you removed scalar FK properties, update any LINQ queries that filtered on them:

```csharp
// v2.2 — filter on scalar FK
.Where(o => o.CustomerId == customerId)

// v2.3 — navigate through the navigation property
.Where(o => o.Customer.Id == customerId)
```

EF Core translates both to identical SQL. The choice is stylistic if you keep scalar FK properties, required if you remove them.

### 3. Optional FK queries — null guard required

When querying optional navigations (`Customer?`), guard against null before accessing properties:

```csharp
// v2.2 — filter on nullable scalar FK
.Where(r => r.CustomerId == customerId)
.Where(r => r.CustomerId == null)

// v2.3 — navigate through the nullable navigation with null guard
.Where(r => r.Customer != null && r.Customer.Id == customerId)
.Where(r => r.Customer == null)
```

### 4. Test entity factories — use navigation properties

If test helper methods set scalar FK properties, update them to set navigation properties:

```csharp
// v2.2
protected static Customer NewCustomer(Address address) => new()
{
    Name      = "Alice",
    AddressId = address.Id,   // ← remove
    Address   = address
};

// v2.3
protected static Customer NewCustomer(Address address) => new()
{
    Name    = "Alice",
    Address = address
};
```

```csharp
// v2.2
protected static ProductReview NewReview(Product product, int? customerId = null) => new()
{
    Product    = product,
    CustomerId = customerId   // ← remove
};

// v2.3 — pass the Customer object instead of the ID
protected static ProductReview NewReview(Product product, Customer? customer = null) => new()
{
    Product  = product,
    Customer = customer
};
```

### 5. Dead code removed

`GetCollectionProperties` and `IsCollectionPropertyOf` were removed from `EntityConventionBuilder` — they were internal helpers unreferenced since the v2.2 relationship refactor. If you subclassed `EntityConventionBuilder` and called these methods (unlikely — both were `private static`), remove those calls.

---

## Combined upgrade checklist — v2.1 to v2.3

Work through this list in order. Each item is a find-and-replace or a targeted code change.

### Domain entities

- [ ] Rename `CreatedAt` → `CreatedDate` on all `IAuditable` entities
- [ ] Rename `ModifiedAt` → `ModifiedDate` on all `IAuditable` entities
- [ ] Rename `DeletedAt` → `DeletedDate` on all `ISoftDelete` entities
- [ ] Optionally remove scalar FK properties (`CategoryId`, `CustomerId`, `AddressId` etc.) and rely on nullable reference type annotations
- [ ] Confirm `<Nullable>enable</Nullable>` is in the domain project's `.csproj`

### Builder configuration

- [ ] Rename `cols.CreatedAt` → `cols.CreatedDate` in any `WithAuditFields()` overrides
- [ ] Rename `cols.ModifiedAt` → `cols.ModifiedDate` in any `WithAuditFields()` overrides
- [ ] Rename `cols.DeletedAt` → `cols.DeletedDate` in any `WithSoftDelete()` overrides

### SQL schema / migrations

- [ ] Update audit column names: `CreatedAt` → `CreatedDate`, `ModifiedAt` → `ModifiedDate`, `DeletedAt` → `DeletedDate`
- [ ] Update FK column names: `customer_id` → `customer`, `address_id` → `address` etc. (or `CustomerId` → `Customer` under PascalCase)
- [ ] Update any indexes that reference the old FK column names
- [ ] Update any stored procedures or raw SQL that reference old column names

### Service and query code

- [ ] If scalar FK properties were removed, update LINQ filters from `.Where(o => o.CustomerId == id)` to `.Where(o => o.Customer.Id == id)`
- [ ] Add null guards for optional FK navigations: `.Where(r => r.Customer != null && r.Customer.Id == id)`
- [ ] Update any direct property assignments: `entity.CreatedAt` → `entity.CreatedDate`, `entity.DeletedAt` → `entity.DeletedDate`

### Tests

- [ ] Update test entity factory methods — remove scalar FK properties, set navigation properties instead
- [ ] Update `NewReview(product, customerId: x)` → `NewReview(product, customer: customerObject)`
- [ ] Update any test assertions that check `CreatedAt`, `ModifiedAt`, `DeletedAt` property names
- [ ] Add `EnableServiceProviderCaching(false)` to any in-memory `DbContext` used in tests if not already present

---

## Keeping v2.1 column names

If your database schema uses the v2.1 column names (`CreatedAt`, `ModifiedAt`, `DeletedAt`) and you cannot migrate the schema right now, override the defaults in the builder:

```csharp
configureConventions: b => b
    .WithAuditFields(cols =>
    {
        cols.CreatedDate  = "CreatedAt";   // restore v2.1 column names
        cols.ModifiedDate = "ModifiedAt";
    })
    .WithSoftDelete(cols =>
    {
        cols.DeletedDate = "DeletedAt";    // restore v2.1 column name
    })
```

This lets you upgrade the library without touching your database schema — migrate the schema in a separate step when convenient.

---

## Keeping scalar FK properties

Scalar FK properties are optional in v2.3 but still fully supported. Keep them if:

- You need to set FKs without loading the navigation (bulk operations, performance-sensitive paths)
- Your team prefers the `CustomerId` style for LINQ filtering
- You want to defer the domain model cleanup to a future sprint

Both styles work correctly and can be mixed within the same domain — some entities with scalar FKs, others without.

---

## Next steps

- [[Getting Started]] — complete walkthrough for new projects
- [[Relationships]] — required/optional detection rules in v2.3
- [[Audit Stamping]] — `IAuditable` and `AuditInterceptor`
- [[Soft Delete]] — `ISoftDelete` and the global query filter
