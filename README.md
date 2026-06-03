[![NuGet](https://img.shields.io/nuget/v/EFConventionBuilder)](https://nuget.org/packages/EFConventionBuilder)
[![NuGet Downloads](https://img.shields.io/nuget/dt/EFConventionBuilder)](https://nuget.org/packages/EFConventionBuilder)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

# EFConvention

**NuGet:** `dotnet add package EFConventionBuilder`

A lightweight **EF Core convention builder** for .NET. Drop it into any project to automatically discover domain entities, map table and column names, wire foreign key relationships, enforce soft delete, and stamp audit fields — all without writing repetitive `OnModelCreating` boilerplate.

---

## Breaking changes

### Upgrading from v2.1 to v2.3

**Property renames (v2.2):**

| v2.1 | v2.2+ | Where |
|---|---|---|
| `IAuditable.CreatedAt` | `IAuditable.CreatedDate` | Interface + domain entities |
| `IAuditable.ModifiedAt` | `IAuditable.ModifiedDate` | Interface + domain entities |
| `ISoftDelete.DeletedAt` | `ISoftDelete.DeletedDate` | Interface + domain entities |
| `AuditColumnNames.CreatedAt` | `AuditColumnNames.CreatedDate` | Builder configuration |
| `AuditColumnNames.ModifiedAt` | `AuditColumnNames.ModifiedDate` | Builder configuration |
| `SoftDeleteColumnNames.DeletedAt` | `SoftDeleteColumnNames.DeletedDate` | Builder configuration |

**Default column names (v2.2):**

| Property | v2.1 default | v2.2+ default |
|---|---|---|
| `CreatedDate` | `CreatedAt` | `CreatedDate` |
| `CreatedBy` | `CreatedBy` | `CreatedBy` |
| `ModifiedDate` | `ModifiedAt` | `ModifiedDate` |
| `ModifiedBy` | `ModifiedBy` | `ModifiedBy` |
| `DeletedDate` | `DeletedAt` | `DeletedDate` |
| `DeletedBy` | `DeletedBy` | `DeletedBy` |

**FK column naming (v2.2):**

FK columns are now named after the navigation property (`Customer`) rather than the type with Id suffix (`CustomerId`). Update any hand-written SQL scripts that reference FK column names.

**Scalar FK properties (v2.3):**

Scalar FK properties (`AddressId`, `CustomerId`, `CategoryId` etc.) are no longer required on domain objects. Required/optional relationships are now detected via nullable reference type annotations:

```csharp
// v2.1 — scalar FK required
public int     AddressId { get; set; }
public Address Address   { get; set; } = null!;

// v2.3 — scalar FK optional, nullability drives required/optional
public Address  Address  { get; set; } = null!;  // non-nullable → required
public Address? Address  { get; set; }            // nullable    → optional
```

Scalar FK properties are still supported for backwards compatibility. Remove them gradually or leave them in place — both work correctly.

**New in v2.2:**
- FK columns named after navigation property (`Customer` not `CustomerId`)
- Multiple collections of the same type resolved by `StartsWith` name convention
- `ForTypes(params Type[] types)` factory method

**New in v2.3:**
- Nullable reference type convention for required/optional detection — no scalar FK properties needed
- `GetCollectionProperties` dead code removed
- `IsCollectionPropertyOf` dead code removed

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

All classes implementing `IEntityBase` or `IEntity` are discovered automatically. No base class inheritance required. Scalar FK properties are optional — required/optional detection uses nullable reference type annotations.

```csharp
public class Customer : IEntity, IAuditable
{
    public int    Id    { get; set; }
    public string Name  { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    // Non-nullable → required FK, DB column: "Address"
    public Address Address { get; set; } = null!;

    // Private setter — enforced by startup validation
    public ICollection<Order> Orders { get; private set; } = new List<Order>();

    // IAuditable — stamped automatically by AuditInterceptor
    public DateTime  CreatedDate  { get; set; }
    public string    CreatedBy    { get; set; } = string.Empty;
    public DateTime? ModifiedDate { get; set; }
    public string?   ModifiedBy   { get; set; }
}

public class Order : IEntity, IAuditable, ISoftDelete
{
    public int      Id          { get; set; }
    public DateTime OrderDate   { get; set; }
    public string   Status      { get; set; } = "Pending";

    [Precision(18, 2)]
    public decimal TotalAmount { get; set; }

    // Non-nullable → required FK, DB column: "Customer"
    public Customer Customer { get; set; } = null!;

    // Private setter — enforced by startup validation
    public ICollection<OrderItem> Items { get; private set; } = new List<OrderItem>();

    // IAuditable
    public DateTime  CreatedDate  { get; set; }
    public string    CreatedBy    { get; set; } = string.Empty;
    public DateTime? ModifiedDate { get; set; }
    public string?   ModifiedBy   { get; set; }

    // ISoftDelete
    public bool      IsDeleted   { get; set; }
    public DateTime? DeletedDate { get; set; }
    public string?   DeletedBy   { get; set; }
}

// Optional FK — nullable navigation property
public class ProductReview : IEntity, IAuditable
{
    public int    Id      { get; set; }
    public int    Rating  { get; set; }
    public string Comment { get; set; } = string.Empty;

    // Non-nullable → required FK
    public Product Product { get; set; } = null!;

    // Nullable → optional FK — review survives customer deletion
    public Customer? Customer { get; set; }

    public DateTime  CreatedDate  { get; set; }
    public string    CreatedBy    { get; set; } = string.Empty;
    public DateTime? ModifiedDate { get; set; }
    public string?   ModifiedBy   { get; set; }
}
```

### 2. Implement `ICurrentUserService`

Defined in the library — implement it once in your application. Only required when using audit stamping via `WithAuditFields()` or `WithFullAudit()`.

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

See [ICurrentUserService — hosting environments](#icurrentuserservice--hosting-environments) for implementations covering WPF, Blazor, JWT, Windows auth, and more.

### 3. Subclass `UnitOfWork`

```csharp
public sealed class StoreDb : UnitOfWork
{
    private readonly string _connectionString;
    private readonly AuditInterceptor _auditInterceptor;

    // typeof(Customer) is just an assembly scan anchor —
    // any domain type works. Unrelated to ICurrentUserService.
    public StoreDb(string connectionString, ICurrentUserService currentUser)
        : base(
            domainAssembly:       typeof(Customer).Assembly,
            configureConventions: b => b.WithFullAudit())
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

// No audit stamping needed — ICurrentUserService not required
public sealed class SimpleDb : UnitOfWork
{
    private readonly string _connectionString;

    public SimpleDb(string connectionString)
        : base(
            domainAssembly:       typeof(Product).Assembly,
            configureConventions: b => b.UseSnakeCase())  // no WithAuditFields
    {
        _connectionString = connectionString;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        if (!options.IsConfigured)
            options.UseSqlServer(_connectionString);
        // No AddInterceptors needed
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

    public async Task<IReadOnlyList<Order>> GetOrdersByCustomerAsync(
        int customerId, CancellationToken ct = default) =>
        await UnitOfWork.Query<Order>()
            .Where(o => o.Customer.Id == customerId)  // navigate through Customer
            .OrderByDescending(o => o.OrderDate)
            .ToListAsync(ct);

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

**Querying optional FK navigations in LINQ:**

```csharp
// Optional FK — guard against null before accessing properties
// EF Core translates this to efficient SQL automatically
public async Task<IReadOnlyList<ProductReview>> GetReviewsByCustomerAsync(
    int? customerId, CancellationToken ct = default)
{
    var query = UnitOfWork.Query<ProductReview>().Include(r => r.Product);

    return customerId.HasValue
        ? await query
            .Where(r => r.Customer != null && r.Customer.Id == customerId)
            .ToListAsync(ct)
        : await query
            .Where(r => r.Customer == null)
            .ToListAsync(ct);
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

## ICurrentUserService — hosting environments

`ICurrentUserService` is only required when audit stamping is enabled via `WithAuditFields()` or `WithFullAudit()`. If you don't use audit stamping, you never need to implement or register it. The assembly anchor (`typeof(Customer).Assembly`) is completely unrelated to identity — it just tells the builder which assembly to scan for domain entities.

### ASP.NET Core MVC or Razor Pages

```csharp
public class HttpContextCurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _accessor;
    public HttpContextCurrentUserService(IHttpContextAccessor accessor)
        => _accessor = accessor;
    public string? UserName => _accessor.HttpContext?.User?.Identity?.Name;
}

// Program.cs
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, HttpContextCurrentUserService>();
```

### ASP.NET Core Web API with JWT

```csharp
public class JwtCurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _accessor;
    public JwtCurrentUserService(IHttpContextAccessor accessor)
        => _accessor = accessor;
    public string? UserName =>
        _accessor.HttpContext?.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? _accessor.HttpContext?.User?.FindFirst("sub")?.Value;
}

// Program.cs
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, JwtCurrentUserService>();
```

### Blazor Server

```csharp
public class BlazorCurrentUserService : ICurrentUserService
{
    private readonly AuthenticationStateProvider _authState;
    public BlazorCurrentUserService(AuthenticationStateProvider authState)
        => _authState = authState;

    public string? UserName
    {
        get
        {
            var state = _authState.GetAuthenticationStateAsync().GetAwaiter().GetResult();
            return state.User?.Identity?.Name;
        }
    }
}

// Program.cs
builder.Services.AddScoped<ICurrentUserService, BlazorCurrentUserService>();
```

### WPF / WinForms / MVVM desktop app

```csharp
// Option 1 — Windows authentication (domain-joined apps)
public class WindowsCurrentUserService : ICurrentUserService
{
    public string? UserName =>
        System.Security.Principal.WindowsIdentity.GetCurrent().Name;
}

// Option 2 — custom login session
public class SessionCurrentUserService : ICurrentUserService
{
    private readonly ISessionService _session;
    public SessionCurrentUserService(ISessionService session)
        => _session = session;
    public string? UserName => _session.CurrentUser?.Username;
}
```

### Background jobs and hosted services

```csharp
public class SystemUserService : ICurrentUserService
{
    public string? UserName => "system";
}

// Program.cs
services.AddScoped<ICurrentUserService, SystemUserService>();
```

### Console apps and CLI tools

```csharp
public class ConsoleCurrentUserService : ICurrentUserService
{
    public string? UserName { get; }
    public ConsoleCurrentUserService(string userName) => UserName = userName;
}

// Program.cs
var userName = args.FirstOrDefault() ?? Environment.UserName;
services.AddSingleton<ICurrentUserService>(new ConsoleCurrentUserService(userName));
```

### Unit tests

```csharp
public class FixedUserService : ICurrentUserService
{
    public string? UserName { get; }
    public FixedUserService(string name = "test-user") => UserName = name;
}
```

### No audit stamping needed

Don't call `WithAuditFields()` or `WithFullAudit()`. `ICurrentUserService` is never referenced and does not need to be registered:

```csharp
public sealed class SimpleDb : UnitOfWork
{
    public SimpleDb(string connectionString)
        : base(
            domainAssembly:       typeof(Product).Assembly,
            configureConventions: b => b.UseSnakeCase())  // no WithAuditFields
    { ... }
}

// DI — no ICurrentUserService needed
services.AddScoped<IUnitOfWork>(sp => new SimpleDb(connectionString));
```

---

## IUnitOfWork

All services depend on `IUnitOfWork` rather than the concrete `StoreDb`, enabling clean unit tests with a mock. The interface exposes:

```csharp
// Transaction boundary
Task CompleteAsync(CancellationToken ct = default);

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

Automatically chooses the correct delete path at runtime:

- Entity implements `ISoftDelete` → sets `IsDeleted = true`, stamps `DeletedDate` / `DeletedBy`, saves. Row retained, hidden by global query filter.
- Entity does not implement `ISoftDelete` → calls `Remove`, saves. Row permanently deleted.

### `RestoreAsync` — reverse a soft delete

Clears all `ISoftDelete` fields, making the row visible to normal queries again. Throws `InvalidOperationException` if the entity does not implement `ISoftDelete`.

### `PurgeAsync` — permanent removal after soft delete

Permanently removes a soft-deleted row. Enforces a two-step pattern — the entity must already be soft-deleted.

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

`SaveChangesInterceptor` that stamps `IAuditable` fields on every save. Registered via `AddInterceptors()` in your concrete `UnitOfWork` subclass.

```
EntityState.Added    → CreatedDate = UtcNow,  CreatedBy = UserName
EntityState.Modified → ModifiedDate = UtcNow, ModifiedBy = UserName
```

Falls back to `"system"` when `ICurrentUserService.UserName` is null.

---

## Naming conventions

Three built-in strategies. Pass a custom `IEntityNamingConvention` implementation to `UseNamingConvention()` for anything else.

| Method | Table | Column | FK |
|---|---|---|---|
| *(default)* | `Customer` | `OrderDate` | `Customer` |
| `UseSnakeCase()` | `customer` | `order_date` | `customer` |
| `UsePluralizedTables()` | `Customers` | `OrderDate` | `Customer` |
| `UseNamingConvention(custom)` | your choice | your choice | your choice |

FK columns are named after the **navigation property name** — not the type name with an `Id` suffix:

```
Order.Customer      → FK column "Customer"
Order.Payee         → FK column "Payee"
Order.OnBehalfOf    → FK column "OnBehalfOf"
```

### Custom naming convention

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
```

---

## Relationship detection

Required/optional detection uses a three-step priority chain:

| Priority | Condition | Result |
|---|---|---|
| 1 | `[Required]` attribute on navigation | `IsRequired(true)` |
| 2 | Scalar FK property `int` (non-nullable) | `IsRequired(true)` |
| 2 | Scalar FK property `int?` (nullable) | `IsRequired(false)` |
| 3 | Non-nullable navigation `Address Address` | `IsRequired(true)` |
| 3 | Nullable navigation `Address? Address` | `IsRequired(false)` |

Scalar FK properties are optional in v2.3 — nullable reference type annotations drive required/optional detection when no scalar FK exists.

### Multiple collections of the same type (v2.2+)

When a principal entity has multiple collections of the same dependent type, the builder resolves the correct inverse using a `StartsWith` name convention:

```csharp
public class Withholding : IEntity
{
    public int Id { get; set; }

    public Person  Person     { get; set; } = null!;  // required — non-nullable
    public Person  Payee      { get; set; } = null!;  // required — non-nullable
    public Person? OnBehalfOf { get; set; }            // optional — nullable
}

public class Person : IEntity
{
    public int Id { get; set; }

    // "PersonWithholdings".StartsWith("Person")         → Person nav
    public ICollection<Withholding> PersonWithholdings
        { get; private set; } = new List<Withholding>();

    // "PayeeWithholdings".StartsWith("Payee")           → Payee nav
    public ICollection<Withholding> PayeeWithholdings
        { get; private set; } = new List<Withholding>();

    // "OnBehalfOfWithholdings".StartsWith("OnBehalfOf") → OnBehalfOf nav
    public ICollection<Withholding> OnBehalfOfWithholdings
        { get; private set; } = new List<Withholding>();
}
```

---

## Optional behaviours

### Soft delete — `WithSoftDelete()`

Entities implementing `ISoftDelete` receive an automatic global query filter (`WHERE IsDeleted = 0`).

```csharp
// Query deleted rows
var deletedOrders = await unitOfWork.Query<Order>()
    .IgnoreQueryFilters()
    .Where(o => o.IsDeleted)
    .ToListAsync();

// Override column names for legacy schemas
.WithSoftDelete(cols =>
{
    cols.IsDeleted   = "Archived";
    cols.DeletedDate = "ArchivedDate";
    cols.DeletedBy   = "ArchivedBy";
})
```

### Audit fields — `WithAuditFields()`

```csharp
.WithAuditFields(cols =>
{
    cols.CreatedDate  = "RecordCreatedDate";
    cols.CreatedBy    = "RecordCreatedUser";
    cols.ModifiedDate = "RecordModifiedDate";
    cols.ModifiedBy   = "RecordModifiedUser";
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

`EntityConventionBuilder.Apply()` validates the domain model at startup and throws `InvalidOperationException` listing all problems together:

```
EFConvention — 2 configuration error(s) detected:
  1. Customer.Orders has a public setter. Collection navigation properties
     must use 'private set;' or no setter.
  2. Product.Category must be virtual. Lazy loading proxies require virtual
     navigation properties.
```

---

## Decimal precision

```csharp
[Precision(18, 2)]
public decimal Price { get; set; }
```

---

## All valid configuration patterns

```csharp
// Bare minimum — PascalCase naming, no audit
EntityConventionBuilder.ForAssemblyOf<Customer>();

// Scan a specific assembly
EntityConventionBuilder.ForAssembly(typeof(Customer).Assembly);

// Register only specific types — useful for testing or partial registration
EntityConventionBuilder.ForTypes(typeof(Customer), typeof(Order), typeof(Address));

// PostgreSQL — snake_case
EntityConventionBuilder.ForAssemblyOf<Customer>().UseSnakeCase();

// Pluralized tables, no audit
EntityConventionBuilder.ForAssemblyOf<Customer>().UsePluralizedTables();

// Soft delete only
EntityConventionBuilder.ForAssemblyOf<Customer>().WithSoftDelete();

// Audit stamping only
EntityConventionBuilder.ForAssemblyOf<Customer>().WithAuditFields();

// PascalCase + full audit (default convention — SQL Server)
EntityConventionBuilder.ForAssemblyOf<Customer>().WithFullAudit();

// snake_case + full audit (PostgreSQL)
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
    .WithAuditFields(cols =>
    {
        cols.CreatedDate  = "RecordCreatedDate";
        cols.CreatedBy    = "RecordCreatedUser";
        cols.ModifiedDate = "RecordModifiedDate";
        cols.ModifiedBy   = "RecordModifiedUser";
    })
    .WithSoftDelete(cols =>
    {
        cols.IsDeleted   = "Archived";
        cols.DeletedDate = "ArchivedDate";
        cols.DeletedBy   = "ArchivedBy";
    });

// Lazy loading validation enabled
EntityConventionBuilder
    .ForAssemblyOf<Customer>()
    .WithFullAudit()
    .WithLazyLoadingValidation();
```

---

## Notes

- **EF version**: Targets EF Core 8+.
- **Nullable reference types**: Requires `<Nullable>enable</Nullable>` in your project file for the v2.3 nullable navigation convention to work correctly. Without it all navigations are treated as optional.
- **`base.OnModelCreating` order**: Always call `base.OnModelCreating(modelBuilder)` before `_conventions.Apply(modelBuilder)` in your `UnitOfWork` subclass so the library conventions take precedence over EF Core defaults.
- **Scalar FK properties**: Optional in v2.3. Keep them if you need to set the FK without loading the navigation (e.g. `order.CustomerId = 5` without loading `Customer`). Remove them for cleaner domain objects.
- **FK column naming**: FK columns are named after the navigation property (`Customer`) not the type with Id suffix (`CustomerId`). EF Core creates a shadow property internally — the library renames only the database column.
- **Querying optional navigations in LINQ**: Guard against null before accessing properties — `r.Customer != null && r.Customer.Id == id`. EF Core translates this to efficient SQL automatically.
- **Multiple collections disambiguation**: Name each collection starting with the corresponding navigation property name on the dependent — e.g. `PersonWithholdings` for the `Person` navigation.
- **`ForTypes` factory**: Use `EntityConventionBuilder.ForTypes(typeof(A), typeof(B))` to register a specific subset of entities. Useful in tests and bounded contexts.
- **Scalar type detection**: Recognises `string`, `DateTime`, `DateTimeOffset`, `decimal`, `Guid`, `bool`, and primitive numeric types plus nullable variants. Custom value objects require explicit `IEntityTypeConfiguration<T>`.
- **Advanced pluralisation**: The built-in `PluralizedNamingConvention` covers basic English rules. For irregular or non-English nouns implement `IEntityNamingConvention` and use [Humanizer](https://github.com/Humanizr/Humanizer).
- **Per-entity overrides**: Call `modelBuilder.Entity<T>()` after `_conventions.Apply(modelBuilder)` to override any convention-generated mapping.
- **Reserved SQL words**: `Order` → `[Order]` under PascalCase, `order` → `[order]` under snake_case in hand-written SQL.
- **Services working with multiple entities**: `ServiceBase<TEntity>` sets the primary entity type for delete/restore/purge only. Services can freely query and modify any entity type through `UnitOfWork.Query<T>()` — all changes commit atomically in a single `CompleteAsync` call.
- **`ICurrentUserService` vs assembly anchor**: The assembly anchor (`typeof(Customer).Assembly`) and `ICurrentUserService` are completely independent. The anchor tells the builder where to find domain entities. `ICurrentUserService` provides user identity for audit stamping. Neither requires the other.