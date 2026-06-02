# EFConvention

A lightweight EF Core convention-over-configuration library. Drop it into any project to automatically discover domain entities, map table and column names, wire foreign key relationships, enforce soft delete, and stamp audit fields — all without writing repetitive `OnModelCreating` boilerplate.

---

## Breaking changes in v2.2

If you are upgrading from v2.1, update your domain entities and any code referencing these members:

| v2.1 | v2.2 | Where |
|---|---|---|
| `IAuditable.CreatedAt` | `IAuditable.CreatedDate` | Interface + domain entities |
| `IAuditable.ModifiedAt` | `IAuditable.ModifiedDate` | Interface + domain entities |
| `ISoftDelete.DeletedAt` | `ISoftDelete.DeletedDate` | Interface + domain entities |
| `AuditColumnNames.CreatedAt` | `AuditColumnNames.CreatedDate` | Builder configuration |
| `AuditColumnNames.ModifiedAt` | `AuditColumnNames.ModifiedDate` | Builder configuration |
| `SoftDeleteColumnNames.DeletedAt` | `SoftDeleteColumnNames.DeletedDate` | Builder configuration |

Default column names also changed:

| Property | v2.1 default column | v2.2 default column |
|---|---|---|
| `CreatedDate` | `CreatedAt` | `CreatedDate` |
| `CreatedBy` | `CreatedBy` | `CreatedBy` |
| `ModifiedDate` | `ModifiedAt` | `ModifiedDate` |
| `ModifiedBy` | `ModifiedBy` | `ModifiedBy` |
| `DeletedDate` | `DeletedAt` | `DeletedDate` |
| `DeletedBy` | `DeletedBy` | `DeletedBy` |

**New in v2.2:**
- FK columns are now named after the navigation property (`Customer`) rather than the type with Id suffix (`CustomerId`)
- Multiple collections of the same type on the principal are now resolved automatically using a `StartsWith` name convention
- `ForTypes(params Type[] types)` factory method for registering a specific subset of entity types

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
    public DateTime  CreatedDate  { get; set; }
    public string    CreatedBy    { get; set; } = string.Empty;
    public DateTime? ModifiedDate { get; set; }
    public string?   ModifiedBy   { get; set; }
}

public class Order : IEntity, IAuditable, ISoftDelete
{
    public int      Id         { get; set; }
    public DateTime OrderDate  { get; set; }
    public int      CustomerId { get; set; }
    public Customer Customer   { get; set; } = null!;

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

## ICurrentUserService — hosting environments

`ICurrentUserService` is only required when audit stamping is enabled via `WithAuditFields()` or `WithFullAudit()`. If you don't use audit stamping, you never need to implement or register it. For applications that do use audit stamping, the implementation varies by hosting environment.

### ASP.NET Core MVC or Razor Pages

Resolves the username from the authenticated user on the current HTTP request:

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

Resolves the username from JWT claims — use the claim type that your token provider stamps:

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

Resolves from the `AuthenticationStateProvider` which tracks the authenticated user across the SignalR circuit:

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

Resolves from your application's session or authentication state. The implementation depends on how your app manages identity:

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

// Option 3 — static app-level user (simple desktop apps)
public class AppCurrentUserService : ICurrentUserService
{
    public string? UserName => App.Current.Properties["LoggedInUser"] as string;
}
```

### Background jobs and hosted services

Use `SystemUserService` which always returns `"system"`. Suitable for any process that runs without a human user context:

```csharp
public class SystemUserService : ICurrentUserService
{
    public string? UserName => "system";
}

// Program.cs — background job host
services.AddScoped<ICurrentUserService, SystemUserService>();
```

### Console apps and CLI tools

Same as background jobs — use `SystemUserService`, or prompt for a username at startup:

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

Use a fixed-identity implementation for deterministic audit field assertions:

```csharp
public class FixedUserService : ICurrentUserService
{
    public string? UserName { get; }
    public FixedUserService(string name = "test-user") => UserName = name;
}

// In test setup
var userService = new FixedUserService("test-user");
var db = new InMemoryStoreDb(userService);
```

### No audit stamping needed

If your application doesn't need audit stamping, don't call `WithAuditFields()` or `WithFullAudit()`. Your entities don't implement `IAuditable`, you don't register `AuditInterceptor`, and `ICurrentUserService` is never referenced:

```csharp
// No ICurrentUserService needed
public sealed class MyDb : UnitOfWork
{
    public MyDb(string connectionString)
        : base(
            domainAssembly:       typeof(Customer).Assembly,
            configureConventions: b => b.UseSnakeCase())  // no WithAuditFields
    {
        _connectionString = connectionString;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        if (!options.IsConfigured)
            options.UseSqlServer(_connectionString);
        // no AddInterceptors needed
    }
}
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

```csharp
protected async Task DeleteAsync(TEntity entity, CancellationToken ct = default)
```

Automatically chooses the correct delete path at runtime:

- Entity implements `ISoftDelete` → sets `IsDeleted = true`, stamps `DeletedDate` / `DeletedBy`, saves. Row retained, hidden by global query filter.
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
EntityState.Added    → CreatedDate = UtcNow,  CreatedBy = UserName
EntityState.Modified → ModifiedDate = UtcNow, ModifiedBy = UserName
```

Fires on both `SavingChanges` and `SavingChangesAsync`, covering all save paths. Falls back to `"system"` when `ICurrentUserService.UserName` is null.

---

## Naming conventions

Three built-in strategies. Pass a custom `IEntityNamingConvention` implementation to `UseNamingConvention()` for anything else.

| Method | Table | Column | FK |
|---|---|---|---|
| *(default)* | `Customer` | `OrderDate` | `Customer` |
| `UseSnakeCase()` | `customer` | `order_date` | `customer` |
| `UsePluralizedTables()` | `Customers` | `OrderDate` | `Customer` |
| `UseNamingConvention(custom)` | your choice | your choice | your choice |

FK columns are named after the **navigation property name** — not the type name with an `Id` suffix. This is more expressive when multiple navigations reference the same type:

```
Withholding.Person      → FK column "Person"
Withholding.Payee       → FK column "Payee"
Withholding.OnBehalfOf  → FK column "OnBehalfOf"
```

Under snake_case these become `person`, `payee`, `on_behalf_of`.

### Custom naming convention

```csharp
// Example: prefixed tables → tbl_Customer, tbl_Order
public class PrefixedNamingConvention : IEntityNamingConvention
{
    private readonly string _prefix;
    public PrefixedNamingConvention(string prefix) => _prefix = prefix;

    public string GetTableName(Type entityType)                       => $"{_prefix}{entityType.Name}";
    public string GetColumnName(PropertyInfo property)                => property.Name;
    public string GetForeignKeyName(PropertyInfo nav, Type principal) => nav.Name;
    public string ApplyToName(string logicalName)                     => logicalName;
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
| `int PersonId` (non-nullable) | FK scalar is non-nullable value type | `IsRequired(true)` |
| `int? PersonId` (nullable) | FK scalar is nullable | `IsRequired(false)` |
| `[Required]` on navigation | Attribute present | `IsRequired(true)` |
| Bi-directional | Both collection and reference found | `WithOne(collectionName)` |
| Uni-directional | No inverse collection found | `WithOne()` |

### Multiple collections of the same type (new in v2.2)

When a principal entity has multiple collections of the same dependent type, the builder resolves the correct inverse using a `StartsWith` name convention — no attributes or explicit configuration required.

```csharp
// Withholding has three navigations to Person:
public class Withholding : IEntity
{
    public int Id { get; set; }

    [Required]
    public Person Person     { get; set; } = null!;  // FK column: Person
    [Required]
    public Person Payee      { get; set; } = null!;  // FK column: Payee
    public Person OnBehalfOf { get; set; } = null!;  // FK column: OnBehalfOf
}

// Person has three corresponding collections.
// Name each collection starting with the navigation property name
// on the dependent side — the builder matches them automatically:
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
    cols.IsDeleted   = "Archived";
    cols.DeletedDate = "ArchivedDate";
    cols.DeletedBy   = "ArchivedBy";
})
```

### Audit fields — `WithAuditFields()`

Entities implementing `IAuditable` are automatically stamped by `AuditInterceptor` on every save. Requires `ICurrentUserService` in DI.

Column names can be overridden for legacy schemas:

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
    .UseSnakeCase()
    .WithFullAudit()
    .WithLazyLoadingValidation();
```

---

## Notes

- **EF version**: Targets EF Core 8+. The `HasMany(Type, string)` overload used for non-generic relationship configuration requires EF Core 6 or later.
- **`base.OnModelCreating` order**: Always call `base.OnModelCreating(modelBuilder)` before `_conventions.Apply(modelBuilder)` in your `UnitOfWork` subclass so the library conventions take precedence over EF Core defaults.
- **FK column naming**: FK columns are named after the navigation property (`Customer`) not the type with Id suffix (`CustomerId`). EF Core creates a shadow property named `CustomerId` internally — the library renames only the database column to `Customer`.
- **Multiple collections disambiguation**: When a principal has multiple collections of the same dependent type, name each collection starting with the corresponding navigation property name on the dependent (e.g. `PersonWithholdings` for the `Person` navigation, `PayeeWithholdings` for the `Payee` navigation).
- **`ForTypes` factory**: Use `EntityConventionBuilder.ForTypes(typeof(A), typeof(B))` when you want to register a specific subset of entities rather than scanning an entire assembly. Useful in tests and in applications with multiple bounded contexts in the same assembly.
- **Scalar type detection**: The column naming pass recognises `string`, `DateTime`, `DateTimeOffset`, `decimal`, `Guid`, `bool`, and the primitive numeric types, plus their nullable variants. Custom value objects require explicit `IEntityTypeConfiguration<T>` alongside the conventions.
- **Advanced pluralisation**: The built-in `PluralizedNamingConvention` covers basic English rules. For irregular nouns or non-English domains, implement `IEntityNamingConvention` and use [Humanizer](https://github.com/Humanizr/Humanizer).
- **Per-entity overrides**: Call `modelBuilder.Entity<T>()` after `_conventions.Apply(modelBuilder)` in `OnModelCreating` to override any convention-generated mapping for a specific entity.
- **Reserved SQL words**: Some entity names produce reserved SQL keywords as table names (e.g. `Order` → `order` under snake_case). EF Core handles quoting in generated queries automatically, but hand-written SQL must bracket-quote them: `dbo.[order]`.
- **Services working with multiple entities**: `ServiceBase<TEntity>` sets the primary entity type for delete/restore/purge helpers only. Any service can query and modify multiple entity types freely through `UnitOfWork.Query<T>()` — all changes commit atomically in a single `CompleteAsync` call.