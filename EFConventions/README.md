# EFConvention

A lightweight EF Core convention-over-configuration library. Drop it into any project to automatically discover domain entities, map table and column names, wire foreign key relationships, enforce soft delete, and stamp audit fields — all without writing repetitive `OnModelCreating` boilerplate.

---

## What's in the library

| File | Purpose |
|---|---|
| `IEntity.cs` | `IEntityBase` and `IEntity` — discovery marker interfaces |
| `IBehaviors.cs` | `IAuditable` and `ISoftDelete` — behavioural contracts |
| `Behaviors.cs` | `AuditColumnNames` and `SoftDeleteColumnNames` — column name overrides |
| `ICurrentUserService.cs` | Identity abstraction — implement once in your application |
| `IEntityNamingConvention.cs` | Naming convention interface + three built-in strategies |
| `EntityConventionBuilder.cs` | Core facade — configure and apply all conventions |
| `IUnitOfWork.cs` | Testable data access interface |
| `UnitOfWork.cs` | Abstract EF Core DbContext implementing `IUnitOfWork` |
| `AuditInterceptor.cs` | `SaveChangesInterceptor` that stamps `IAuditable` fields |
| `ServiceBase.cs` | Abstract service base — delete, restore, and purge helpers |

---

## Quick start

### 1. Implement `IEntity` on your domain objects

All classes implementing `IEntityBase` or `IEntity` are discovered automatically. No base class inheritance required.

```csharp
public class Customer : IEntity, IAuditable
{
    public int    Id    { get; set; }
    public string Name  { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    // Required FK — non-nullable int detected automatically
    public int     AddressId { get; set; }
    public Address Address   { get; set; } = null!;

    // Private setter — enforced by startup validation
    public ICollection<Order> Orders { get; private set; } = new List<Order>();

    // IAuditable — stamped automatically by AuditInterceptor
    public DateTime  CreatedAt  { get; set; }
    public string    CreatedBy  { get; set; } = string.Empty;
    public DateTime? ModifiedAt { get; set; }
    public string?   ModifiedBy { get; set; }
}

public class Order : IEntity, IAuditable, ISoftDelete
{
    public int      Id         { get; set; }
    public DateTime OrderDate  { get; set; }
    public int      CustomerId { get; set; }
    public Customer Customer   { get; set; } = null!;

    public ICollection<OrderItem> Items { get; private set; } = new List<OrderItem>();

    // IAuditable
    public DateTime  CreatedAt  { get; set; }
    public string    CreatedBy  { get; set; } = string.Empty;
    public DateTime? ModifiedAt { get; set; }
    public string?   ModifiedBy { get; set; }

    // ISoftDelete
    public bool      IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string?   DeletedBy { get; set; }
}
```

### 2. Implement `ICurrentUserService`

Defined in the library — implement it once in your application:

```csharp
// ASP.NET Core web app
public class HttpContextCurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _accessor;
    public HttpContextCurrentUserService(IHttpContextAccessor accessor)
        => _accessor = accessor;
    public string? UserName => _accessor.HttpContext?.User?.Identity?.Name;
}

// Background job / console app
public class SystemUserService : ICurrentUserService
{
    public string? UserName => "system";
}
```

### 3. Subclass `UnitOfWork`

```csharp
public sealed class StoreDb : UnitOfWork
{
    private readonly string _connectionString;
    private readonly AuditInterceptor _auditInterceptor;

    public StoreDb(string connectionString, ICurrentUserService currentUser)
        : base(
            domainAssembly:       typeof(Customer).Assembly,
            configureConventions: b => b.UseSnakeCase().WithFullAudit())
    {
        _connectionString = connectionString;
        _auditInterceptor = new AuditInterceptor(currentUser);
    }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        if (!options.IsConfigured)
            options
                .UseSqlServer(_connectionString)
                .AddInterceptors(_auditInterceptor);
    }
}
```

### 4. Write application services

```csharp
public sealed class OrderService : ServiceBase<Order>, IOrderService
{
    public OrderService(IUnitOfWork uow, ICurrentUserService user)
        : base(uow, user) { }

    public async Task<Order?> GetOrderAsync(int id, CancellationToken ct = default) =>
        await UnitOfWork.Query<Order>()
            .Include(o => o.Customer)
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id, ct);

    public async Task DeleteOrderAsync(int id, CancellationToken ct = default)
    {
        var order = await UnitOfWork.Query<Order>()
            .FirstOrDefaultAsync(o => o.Id == id, ct)
            ?? throw new KeyNotFoundException($"Order {id} not found.");

        // Order implements ISoftDelete → soft delete chosen automatically.
        // Remove ISoftDelete from Order → this becomes a hard delete.
        await DeleteAsync(order, ct);
    }

    public async Task RestoreOrderAsync(int id, CancellationToken ct = default)
    {
        var order = await UnitOfWork.Query<Order>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(o => o.Id == id && o.IsDeleted, ct)
            ?? throw new KeyNotFoundException($"Soft-deleted order {id} not found.");

        await RestoreAsync(order, ct);
    }

    public async Task PurgeOrderAsync(int id, CancellationToken ct = default)
    {
        var order = await UnitOfWork.Query<Order>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(o => o.Id == id, ct)
            ?? throw new KeyNotFoundException($"Order {id} not found.");

        await PurgeAsync(order, ct);
    }
}
```

### 5. Register with DI

```csharp
// Program.cs
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, HttpContextCurrentUserService>();

builder.Services.AddScoped<IUnitOfWork>(sp =>
    new StoreDb(
        builder.Configuration.GetConnectionString("StoreDb")!,
        sp.GetRequiredService<ICurrentUserService>()));

builder.Services.AddScoped<IOrderService, OrderService>();
```

---

## IUnitOfWork

All services depend on `IUnitOfWork` rather than the concrete `StoreDb`, enabling clean unit tests with a mock. The interface exposes:

```csharp
// Transaction boundary
Task  CompleteAsync(CancellationToken ct = default);

// Query — returns IQueryable for LINQ composition
IQueryable<TEntity> Query<TEntity>() where TEntity : class;

// Commands — modify EF's change tracker, no I/O until CompleteAsync
void Add<TEntity>(TEntity entity)    where TEntity : class;
void Update<TEntity>(TEntity entity) where TEntity : class;
void Remove<TEntity>(TEntity entity) where TEntity : class;

// Lookup — uses EF identity map, avoids round-trip when already tracked
ValueTask<TEntity?> FindAsync<TEntity>(int id, CancellationToken ct = default)
    where TEntity : class;

// Refresh — reload from database, optionally reload navigation properties
Task RefreshAsync<TEntity>(TEntity entity,
    params Expression<Func<TEntity, object>>[] references)
    where TEntity : class;

Task RefreshCollectionAsync<TEntity, TElement>(TEntity entity,
    Expression<Func<TEntity, ICollection<TElement>>> collection,
    CancellationToken ct = default)
    where TEntity : class where TElement : class;
```

`Add`, `Update`, and `Remove` are synchronous because they only touch EF's in-memory change tracker — no database I/O occurs until `CompleteAsync` is called. All database operations are async.

---

## ServiceBase\<TEntity\>

Abstract base for all application services. Provides three protected helpers:

### `DeleteAsync` — runtime soft/hard delete detection

```csharp
protected async Task DeleteAsync(TEntity entity, CancellationToken ct = default)
```

Automatically chooses the correct delete path at runtime:

- Entity implements `ISoftDelete` → sets `IsDeleted = true`, stamps `DeletedAt` / `DeletedBy`, saves. Row retained, hidden by global query filter.
- Entity does not implement `ISoftDelete` → calls `Remove`, saves. Row permanently deleted.

Adding or removing `ISoftDelete` from an entity type silently changes the delete behaviour with no service code changes.

### `RestoreAsync` — reverse a soft delete

```csharp
protected async Task RestoreAsync(TEntity entity, CancellationToken ct = default)
```

Clears all `ISoftDelete` fields, making the row visible to normal queries again. Throws `InvalidOperationException` if the entity does not implement `ISoftDelete`.

### `PurgeAsync` — permanent removal after soft delete

```csharp
protected async Task PurgeAsync(TEntity entity, CancellationToken ct = default)
```

Permanently removes a soft-deleted row. Enforces a two-step pattern — the entity must already be soft-deleted. Throws `InvalidOperationException` if the entity is still active or does not implement `ISoftDelete`.

### Soft delete lifecycle

```
AddOrderAsync         → IsDeleted = false  (active, visible to all queries)
DeleteOrderAsync      → IsDeleted = true   (hidden by global query filter)
GetDeletedOrdersAsync → .IgnoreQueryFilters() to reach them
RestoreOrderAsync     → IsDeleted = false  (visible again)
PurgeOrderAsync       → Physical DELETE    (permanent, requires prior soft-delete)
```

---

## AuditInterceptor

`SaveChangesInterceptor` that stamps `IAuditable` fields on every save. Registered via `AddInterceptors()` in your concrete `UnitOfWork` subclass — the `UnitOfWork` base class itself has no knowledge of `ICurrentUserService`.

```
EntityState.Added    → CreatedAt = UtcNow,  CreatedBy = UserName
EntityState.Modified → ModifiedAt = UtcNow, ModifiedBy = UserName
```

Fires on both `SavingChanges` and `SavingChangesAsync`, covering all save paths. Falls back to `"system"` when `ICurrentUserService.UserName` is null.

---

## Naming conventions

Three built-in strategies. Pass a custom `IEntityNamingConvention` implementation to `UseNamingConvention()` for anything else.

| Method | Table | Column | FK |
|---|---|---|---|
| *(default)* | `Customer` | `OrderDate` | `CustomerId` |
| `UseSnakeCase()` | `customer` | `order_date` | `customer_id` |
| `UsePluralizedTables()` | `Customers` | `OrderDate` | `CustomerId` |
| `UseNamingConvention(custom)` | your choice | your choice | your choice |

### Custom naming convention

```csharp
// Example: prefixed tables → tbl_Customer, tbl_Order
public class PrefixedNamingConvention : IEntityNamingConvention
{
    private readonly string _prefix;
    public PrefixedNamingConvention(string prefix) => _prefix = prefix;

    public string GetTableName(Type entityType)                          => $"{_prefix}{entityType.Name}";
    public string GetColumnName(PropertyInfo property)                   => property.Name;
    public string GetForeignKeyName(PropertyInfo nav, Type principal)    => $"{nav.Name}Id";
    public string ApplyToName(string logicalName)                        => logicalName;
}

// Usage:
EntityConventionBuilder
    .ForAssemblyOf<Customer>()
    .UseNamingConvention(new PrefixedNamingConvention("tbl_"));
```

---

## Relationship detection

Foreign key relationships are inferred automatically from navigation property types and FK scalar property nullability.

| Scenario | Detection rule | Result |
|---|---|---|
| `int CustomerId` (non-nullable) | FK is non-nullable value type | `IsRequired(true)` |
| `int? CustomerId` (nullable) | FK is nullable | `IsRequired(false)` |
| `[Required]` on navigation | Attribute present | `IsRequired(true)` |
| Bi-directional | Both collection and reference found | `WithOne(inverseName)` |
| Uni-directional | Collection side only | `WithOne()` |

---

## Optional behaviours

Both features are opt-in. The builder flag and the entity interface must both be present for the behaviour to apply.

### Soft delete — `WithSoftDelete()`

Entities implementing `ISoftDelete` receive an automatic global query filter (`WHERE IsDeleted = 0`). Deleted rows are never returned without an explicit `IgnoreQueryFilters()` call.

To query deleted rows:

```csharp
var deletedOrders = await unitOfWork.Query<Order>()
    .IgnoreQueryFilters()
    .Where(o => o.IsDeleted)
    .ToListAsync();
```

Column names can be overridden for legacy schemas — the active naming convention is still applied on top:

```csharp
.WithSoftDelete(cols =>
{
    cols.IsDeleted = "Archived";
    cols.DeletedAt = "ArchivedAt";
    cols.DeletedBy = "ArchivedBy";
})
```

### Audit fields — `WithAuditFields()`

Entities implementing `IAuditable` are automatically stamped by `AuditInterceptor` on every save. Requires `ICurrentUserService` in DI.

Column names can be overridden for legacy schemas:

```csharp
.WithAuditFields(cols =>
{
    cols.CreatedAt  = "RecordCreatedDate";
    cols.CreatedBy  = "RecordCreatedUser";
    cols.ModifiedAt = "RecordModifiedDate";
    cols.ModifiedBy = "RecordModifiedUser";
})
```

### Both at once — `WithFullAudit()`

Equivalent to `.WithSoftDelete().WithAuditFields()`.

### Per-entity opt-in matrix

| Entity implements | Builder configured with | Audit columns | Soft-delete columns |
|---|---|---|---|
| neither | anything | No | No |
| `IAuditable` only | `.WithAuditFields()` or `.WithFullAudit()` | Yes | No |
| `ISoftDelete` only | `.WithSoftDelete()` or `.WithFullAudit()` | No | Yes |
| both | `.WithFullAudit()` | Yes | Yes |

---

## Startup validation

`EntityConventionBuilder.Apply()` validates the domain model at startup and throws `InvalidOperationException` listing all problems together if any are found:

```
EFConvention — 2 configuration error(s) detected:
  1. Customer.Orders has a public setter. Collection navigation properties
     must use 'private set;' or no setter.
  2. Product.Category must be virtual. Lazy loading proxies require virtual
     navigation properties.
```

Validated automatically:

- Collection navigation properties must have `private set;` or no setter
- When `WithLazyLoadingValidation()` is enabled, all navigation properties must be `virtual`

---

## Decimal precision

`[Precision(18, 2)]` attributes on `decimal` properties are picked up automatically — no explicit fluent configuration required:

```csharp
[Precision(18, 2)]
public decimal Price { get; set; }

[Precision(18, 2)]
public decimal TotalAmount { get; set; }
```

---

## All valid configuration patterns

```csharp
// Bare minimum — PascalCase naming, no audit
EntityConventionBuilder.ForAssemblyOf<Customer>();

// PostgreSQL — snake_case
EntityConventionBuilder.ForAssemblyOf<Customer>().UseSnakeCase();

// Pluralized tables, no audit
EntityConventionBuilder.ForAssemblyOf<Customer>().UsePluralizedTables();

// Soft delete only
EntityConventionBuilder.ForAssemblyOf<Customer>().WithSoftDelete();

// Audit stamping only
EntityConventionBuilder.ForAssemblyOf<Customer>().WithAuditFields();

// snake_case + full audit (most common)
EntityConventionBuilder.ForAssemblyOf<Customer>().UseSnakeCase().WithFullAudit();

// Custom naming + full audit
EntityConventionBuilder
    .ForAssemblyOf<Customer>()
    .UseNamingConvention(new PrefixedNamingConvention("tbl_"))
    .WithFullAudit();

// Legacy schema — override audit and soft-delete column names
EntityConventionBuilder
    .ForAssemblyOf<Customer>()
    .UseSnakeCase()
    .WithAuditFields(cols => { cols.CreatedAt = "RecordCreatedDate"; })
    .WithSoftDelete(cols => { cols.IsDeleted = "Archived"; });

// Lazy loading validation enabled
EntityConventionBuilder
    .ForAssemblyOf<Customer>()
    .UseSnakeCase()
    .WithFullAudit()
    .WithLazyLoadingValidation();

// Scan a specific assembly rather than anchoring on a type
EntityConventionBuilder
    .ForAssembly(typeof(Customer).Assembly)
    .UseSnakeCase();
```

---

## Notes

- **EF version**: Targets EF Core 8+. The `HasMany(Type, string)` overload used for non-generic relationship configuration requires EF Core 6 or later.
- **`base.OnModelCreating` order**: Always call `base.OnModelCreating(modelBuilder)` before `_conventions.Apply(modelBuilder)` in your `UnitOfWork` subclass so the library conventions take precedence.
- **Scalar type detection**: The column naming pass recognises `string`, `DateTime`, `DateTimeOffset`, `decimal`, `Guid`, `bool`, and the primitive numeric types, plus their nullable variants. Custom value objects require explicit `IEntityTypeConfiguration<T>` alongside the conventions.
- **Advanced pluralisation**: The built-in `PluralizedNamingConvention` covers basic English rules. For irregular nouns or non-English domains, implement `IEntityNamingConvention` and use [Humanizer](https://github.com/Humanizr/Humanizer).
- **Per-entity overrides**: Call `modelBuilder.Entity<T>()` after `_conventions.Apply(modelBuilder)` in `OnModelCreating` to override any convention-generated mapping for a specific entity.
- **Reserved SQL words**: Some entity names produce reserved SQL keywords as table names (e.g. `Order` → `order` under snake_case). EF Core handles quoting in generated queries automatically, but hand-written SQL must bracket-quote them: `dbo.[order]`.
