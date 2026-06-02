# FAQ

Frequently asked questions about EFConventionBuilder.

---

## General

### Do I need to declare DbSet properties on my DbContext?

No. EFConventionBuilder discovers and registers all `IEntityBase` implementations automatically during `Apply()`. You never need to add `DbSet<Customer>`, `DbSet<Order>` etc. to your `UnitOfWork` subclass.

### Does EFConventionBuilder work with EF Core migrations?

Yes — fully. Run migrations as normal:

```bash
dotnet ef migrations add InitialCreate --project YourApp
dotnet ef database update
```

The generated migration SQL reflects your active naming convention — PascalCase, snake_case, or pluralized. EFConventionBuilder just configures the EF Core model; migrations and schema management are handled entirely by EF Core.

### Can I use EFConventionBuilder with PostgreSQL?

Yes. Use `.UseSnakeCase()` — PostgreSQL treats unquoted identifiers as lowercase and snake_case is the standard convention for PostgreSQL schemas:

```csharp
configureConventions: b => b.UseSnakeCase().WithFullAudit()
```

For the database provider add the Npgsql package:

```bash
dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL
```

Then use `.UseNpgsql()` instead of `.UseSqlServer()` in `OnConfiguring`.

### Can I use EFConventionBuilder with SQLite?

Yes. SQLite is fully supported for development and testing. Replace `.UseSqlServer()` with `.UseSqlite()`:

```csharp
options.UseSqlite("Data Source=app.db").AddInterceptors(_auditInterceptor);
```

---

## Domain model

### Do I need virtual properties?

Only if you use EF Core lazy loading proxies. Without lazy loading — which is the default and recommended approach — `virtual` is not required on navigation properties.

```csharp
// Without lazy loading (recommended) — no virtual needed
public Customer Customer { get; set; } = null!;
public ICollection<Order> Orders { get; private set; } = new List<Order>();

// With lazy loading proxies — all navigations must be virtual
public virtual Customer Customer { get; set; } = null!;
public virtual ICollection<Order> Orders { get; private set; } = new List<Order>();
```

To enable lazy loading and have the builder validate virtuality:

```csharp
options.UseLazyLoadingProxies();

configureConventions: b => b
    .WithFullAudit()
    .WithLazyLoadingValidation()  // startup error if any nav is not virtual
```

For most applications explicit `Include()` calls are cleaner and more performant than lazy loading. See [[Relationships]] for more.

### Do I need scalar FK properties like CustomerId?

Not in v2.3. The builder detects required vs optional from nullable reference type annotations on the navigation property:

```csharp
public Customer  Customer { get; set; } = null!;  // required — no scalar FK needed
public Customer? Customer { get; set; }            // optional — no scalar FK needed
```

Keep scalar FK properties when you need to set the FK without loading the navigation:

```csharp
// With scalar FK — no database round-trip
order.CustomerId = customerId;

// Without scalar FK — must load the entity
order.Customer = await UnitOfWork.FindAsync<Customer>(customerId);
```

Both styles work. Mix them freely within the same domain.

### Can I use a base class instead of implementing IEntity directly?

Yes — define your own `EntityBase` in your application:

```csharp
// In your application — not in the library
public abstract class EntityBase : IEntity
{
    public int Id { get; set; }
}

// Domain objects inherit Id automatically
public class Product : EntityBase, IAuditable, ISoftDelete
{
    public string   Name     { get; set; } = string.Empty;
    public Category Category { get; set; } = null!;
    // ... no need to declare Id
}
```

This is a convenience pattern — not required by the library. Avoid it when a class needs to inherit from something else.

### Why does my collection property cause a startup error?

Collection navigation properties must use `private set;` or no setter:

```csharp
// ❌ Startup error — public setter
public ICollection<Order> Orders { get; set; } = new List<Order>();

// ✅ Correct — private setter
public ICollection<Order> Orders { get; private set; } = new List<Order>();

// ✅ Also correct — no setter (init-only or get-only)
public ICollection<Order> Orders { get; } = new List<Order>();
```

A public setter allows external code to replace the entire collection, breaking aggregate encapsulation. The startup error catches this early rather than letting it silently corrupt data.

### Can I have multiple navigations to the same entity type?

Yes — use the `StartsWith` name convention to disambiguate:

```csharp
// Dependent — three navigations to Person
public class Withholding : IEntity
{
    public int    Id         { get; set; }
    public Person Person     { get; set; } = null!;
    public Person Payee      { get; set; } = null!;
    public Person? OnBehalfOf { get; set; }
}

// Principal — three collections named to start with the navigation name
public class Person : IEntity
{
    public int Id { get; set; }
    public ICollection<Withholding> PersonWithholdings     { get; private set; } = new List<Withholding>();
    public ICollection<Withholding> PayeeWithholdings      { get; private set; } = new List<Withholding>();
    public ICollection<Withholding> OnBehalfOfWithholdings { get; private set; } = new List<Withholding>();
}
```

See [[Relationships]] for the full disambiguation rules.

---

## Services

### Can a service work with multiple entity types?

Yes. `ServiceBase<TEntity>` sets the primary entity type for delete/restore/purge helpers only. A service can freely query and modify any entity type through `UnitOfWork`:

```csharp
public sealed class OrderService : ServiceBase<Order>, IOrderService
{
    // Query Customer, Product, Order, OrderItem — all in the same service
    public async Task<Order> PlaceOrderAsync(int customerId, ...)
    {
        var customer = await UnitOfWork.Query<Customer>()...;
        var product  = await UnitOfWork.Query<Product>()...;

        var order = new Order { Customer = customer, ... };
        UnitOfWork.Add(order);

        foreach (var item in items)
            UnitOfWork.Add(item);

        await UnitOfWork.CompleteAsync(ct);
        return order;
    }
}
```

All changes accumulate in the same EF Core change tracker and commit atomically in a single `CompleteAsync` call.

### Should I create one service per entity or organise by business capability?

Organise by **business capability**. An `OrderService` that handles everything related to placing, fulfilling, and cancelling orders is more cohesive than separate `OrderService`, `OrderItemService`, `InventoryService` each knowing only one table.

The unit of work pattern is designed for this — multiple entity types, one transaction.

### What if my service doesn't need delete/restore/purge?

You can still inherit `ServiceBase<TEntity>` — the delete helpers are protected and optional. You don't have to expose them publicly or call them. Alternatively, don't inherit from `ServiceBase` at all and depend only on `IUnitOfWork`:

```csharp
// Without ServiceBase — when you don't need delete/restore/purge
public class ReportService : IReportService
{
    private readonly IUnitOfWork _uow;

    public ReportService(IUnitOfWork uow) => _uow = uow;

    public async Task<SalesSummary> GetSalesSummaryAsync(...)
    {
        // Read-only service — no delete needed
        var orders = await _uow.Query<Order>()...;
        // ...
    }
}
```

---

## Audit stamping

### Do I need to implement ICurrentUserService?

Only when using audit stamping via `WithAuditFields()` or `WithFullAudit()`. If your application doesn't need audit fields, skip `IAuditable`, skip `WithAuditFields()`, and never implement or register `ICurrentUserService`.

### What happens if ICurrentUserService.UserName returns null?

`AuditInterceptor` falls back to `"system"`. This covers background jobs, anonymous operations, and any scenario where identity is not available.

### Can I use different ICurrentUserService implementations in the same application?

Yes — register the implementation that matches your hosting environment. For applications with multiple contexts (e.g. a web API that also runs background jobs), register the appropriate implementation per DI scope:

```csharp
// Web request scope — resolves from HTTP context
builder.Services.AddScoped<ICurrentUserService, HttpContextCurrentUserService>();

// Background job host — always "system"
services.AddScoped<ICurrentUserService, SystemUserService>();
```

---

## Soft delete

### How do I query soft-deleted rows?

Call `.IgnoreQueryFilters()` to bypass the global filter:

```csharp
var deletedOrders = await UnitOfWork.Query<Order>()
    .IgnoreQueryFilters()
    .Where(o => o.IsDeleted)
    .ToListAsync();
```

### Can I permanently delete a soft-deleted entity?

Yes — use `PurgeAsync`. It requires the entity to be soft-deleted first (two-step pattern):

```csharp
await _svc.DeleteOrderAsync(id);   // soft delete first
await _svc.PurgeOrderAsync(id);    // then permanently remove
```

`PurgeAsync` throws if called on an active (non-deleted) entity.

### Can some entities use soft delete while others use hard delete?

Yes. Each entity independently implements or omits `ISoftDelete`. `ServiceBase.DeleteAsync` chooses the correct path at runtime:

```csharp
public class Order   : IEntity, ISoftDelete { ... }  // soft delete
public class Address : IEntity              { ... }  // hard delete
```

No configuration or code change needed in services — the delete path is determined by the entity type.

---

## Naming and schema

### Can I override column names for a specific entity?

Yes — call `modelBuilder.Entity<T>()` after `_conventions.Apply(modelBuilder)` in `OnModelCreating`:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder);
    _conventions.Apply(modelBuilder);

    // Override specific column after conventions are applied
    modelBuilder.Entity<Customer>()
        .Property(c => c.Email)
        .HasColumnName("EmailAddress");
}
```

### Can I use a custom naming convention?

Yes — implement `IEntityNamingConvention`:

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

EntityConventionBuilder
    .ForAssemblyOf<Customer>()
    .UseNamingConvention(new PrefixedNamingConvention("tbl_"));
```

See [[Naming Conventions]] for more examples.

### Why is my Order table causing SQL errors?

`Order` is a reserved SQL Server keyword. EF Core handles quoting in generated queries automatically, but hand-written SQL must bracket-quote it:

```sql
-- Correct
SELECT * FROM dbo.[Order] WHERE IsDeleted = 0

-- Wrong — SQL Server interprets ORDER as a keyword
SELECT * FROM dbo.Order WHERE IsDeleted = 0
```

---

## Testing

### Why are my tests interfering with each other?

Use `Guid.NewGuid().ToString()` as the in-memory database name and `EnableServiceProviderCaching(false)`:

```csharp
options
    .UseInMemoryDatabase(Guid.NewGuid().ToString())  // fresh db per test
    .EnableServiceProviderCaching(false)              // no model caching between tests
```

Without `EnableServiceProviderCaching(false)`, EF Core caches the first model it builds and reuses it for subsequent tests — tests that configure the model differently will see the wrong model.

### Why is ForTypes useful in tests?

`ForAssemblyOf<T>()` scans the entire assembly. In a test project this picks up all test fixture types — including intentionally broken ones like `BadEntity` used in startup validation tests. `ForTypes` lets you register only the specific types needed for a test:

```csharp
EntityConventionBuilder
    .ForTypes(typeof(Principal), typeof(Dependent))
    .Apply(modelBuilder);
```

---

## Next steps

- [[Getting Started]] — complete walkthrough for new projects
- [[Migration Guide]] — upgrading from v2.1 to v2.3
- [[Relationships]] — required/optional detection, multiple collections
- [[Testing]] — unit and integration testing patterns
