# EfConventions

A lightweight EF Core convention-over-configuration library. Drop it into any project to automatically discover domain entities, map table and column names, wire foreign key relationships, enforce soft delete, and stamp audit fields — all without writing repetitive `OnModelCreating` boilerplate.

---

## Files

| File | Purpose |
|---|---|
| `IEntityNamingConvention.cs` | Naming convention interface + three built-in strategies |
| `DomainContracts.cs` | `IAuditable` and `ISoftDelete` interfaces |
| `EntityConventionBuilder.cs` | Core facade — configure and apply all conventions |
| `AppDbContext.cs` | Sample DbContext showing full wiring and DI setup |

---

## Quick start

### 1. Define a base entity

All classes in your domain library must inherit from `Entity` to be discovered.

```csharp
public abstract class Entity
{
    public int Id { get; set; }
}
```

### 2. Configure the builder in your DbContext

```csharp
public class AppDbContext : DbContext
{
    private readonly ICurrentUserService _currentUser;
    private readonly EntityConventionBuilder _conventions;

    public AppDbContext(DbContextOptions<AppDbContext> options, ICurrentUserService currentUser)
        : base(options)
    {
        _currentUser = currentUser;
        _conventions = EntityConventionBuilder
            .ForAssemblyOf<Customer>()   // anchor type in your domain assembly
            .UseSnakeCase()              // optional naming convention
            .WithFullAudit();            // optional soft delete + audit stamping
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        _conventions.Apply(modelBuilder);
        base.OnModelCreating(modelBuilder);
    }

    public override int SaveChanges()
    {
        StampAuditFields();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        StampAuditFields();
        return base.SaveChangesAsync(ct);
    }

    private void StampAuditFields()
    {
        if (!_conventions.IsAuditEnabled) return;
        var user = _currentUser.UserName ?? "system";
        foreach (var entry in ChangeTracker.Entries<IAuditable>())
        {
            if (entry.State == EntityState.Added)
            { entry.Entity.CreatedAt = DateTime.UtcNow; entry.Entity.CreatedBy = user; }
            if (entry.State == EntityState.Modified)
            { entry.Entity.ModifiedAt = DateTime.UtcNow; entry.Entity.ModifiedBy = user; }
        }
    }
}
```

### 3. Register in DI

```csharp
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(connectionString));

builder.Services.AddScoped<ICurrentUserService, HttpContextCurrentUserService>();
```

---

## Naming conventions

Three built-in strategies ship out of the box. Pass a custom implementation to `UseNamingConvention()` for anything else.

| Method | Table | Column | FK |
|---|---|---|---|
| *(default)* | `Customer` | `OrderDate` | `CustomerId` |
| `UseSnakeCase()` | `customer` | `order_date` | `customer_id` |
| `UsePluralizedTables()` | `Customers` | `OrderDate` | `CustomerId` |
| `UseNamingConvention(custom)` | your choice | your choice | your choice |

### Custom naming convention

Implement `IEntityNamingConvention` to define a fully custom strategy. No changes to `EntityConventionBuilder` are required.

```csharp
// Example: prefixed tables → tbl_Customer, tbl_Order
public class PrefixedNamingConvention : IEntityNamingConvention
{
    private readonly string _prefix;
    public PrefixedNamingConvention(string prefix) => _prefix = prefix;

    public string GetTableName(Type entityType) => $"{_prefix}{entityType.Name}";
    public string GetColumnName(PropertyInfo property) => property.Name;
    public string GetForeignKeyName(PropertyInfo nav, Type principal) => $"{nav.Name}Id";
}

// Usage:
EntityConventionBuilder
    .ForAssemblyOf<Customer>()
    .UseNamingConvention(new PrefixedNamingConvention("tbl_"));
```

---

## Relationship detection

Foreign key relationships are inferred automatically from navigation property types. No attributes or explicit configuration required.

| Scenario | Detection rule | Generated FK |
|---|---|---|
| `Order.Customer` (reference to Entity) | Property type is an `Entity` subclass | `CustomerId` on `Order` |
| `Customer.Orders` (collection of Entity) | Generic arg of `List<T>` / `ICollection<T>` is an `Entity` subclass | `CustomerId` on `Order` |
| Bi-directional | Both sides found via reflection | `WithOne(inverseName)` |
| Uni-directional | Only collection side found | `WithOne()` (no inverse) |

---

## Optional behaviors

Both features are opt-in and operate independently. The builder flags act as feature switches at the context level; the interfaces act as opt-in at the entity level. **Both conditions must be true** for the behavior to apply.

### Soft delete — `WithSoftDelete()`

Entities implementing `ISoftDelete` receive an automatic global query filter (`WHERE IsDeleted = 0`). Deleted rows are never returned to callers without an explicit opt-out.

```csharp
public class Order : Entity, ISoftDelete
{
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }
}
```

To query deleted rows at a specific call site:

```csharp
var allOrders = dbContext.Set<Order>().IgnoreQueryFilters().ToList();
```

### Audit fields — `WithAuditFields()`

Entities implementing `IAuditable` are automatically stamped inside `SaveChanges`. Requires `ICurrentUserService` in DI. Falls back to `"system"` if the service returns `null`.

```csharp
public class Customer : Entity, IAuditable
{
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime? ModifiedAt { get; set; }
    public string? ModifiedBy { get; set; }
}
```

### Both at once — `WithFullAudit()`

Equivalent to `.WithSoftDelete().WithAuditFields()`. Use this for the complete audit story: creation, modification, and deletion — all captured automatically.

### Per-entity opt-in matrix

Entities can implement neither, either, or both interfaces independently of each other.

| Entity | Interfaces | Behavior |
|---|---|---|
| `Address` | *(none)* | Plain table, no filtering or stamping |
| `Customer` | `IAuditable` | Audit stamping only |
| `ArchivedLog` | `ISoftDelete` | Soft delete filter only |
| `Order` | `IAuditable`, `ISoftDelete` | Full audit trail |

---

## All valid configuration patterns

```csharp
// Bare minimum — PascalCase naming, no audit
EntityConventionBuilder.ForAssemblyOf<Customer>();

// PostgreSQL project
EntityConventionBuilder.ForAssemblyOf<Customer>().UseSnakeCase();

// Pluralized tables, no audit
EntityConventionBuilder.ForAssemblyOf<Customer>().UsePluralizedTables();

// Soft delete only
EntityConventionBuilder.ForAssemblyOf<Customer>().WithSoftDelete();

// Audit stamping only
EntityConventionBuilder.ForAssemblyOf<Customer>().WithAuditFields();

// Snake case + full audit
EntityConventionBuilder.ForAssemblyOf<Customer>().UseSnakeCase().WithFullAudit();

// Custom naming + full audit
EntityConventionBuilder
    .ForAssemblyOf<Customer>()
    .UseNamingConvention(new PrefixedNamingConvention("tbl_"))
    .WithFullAudit();

// Scan a specific assembly rather than anchoring on a type
EntityConventionBuilder
    .ForAssembly(typeof(Customer).Assembly)
    .UseSnakeCase();
```

---

## ICurrentUserService

The DbContext depends on `ICurrentUserService` to resolve the current user for audit stamping. Provide your own implementation suited to your application's authentication model.

```csharp
public interface ICurrentUserService
{
    string? UserName { get; }
}

// ASP.NET Core example
public class HttpContextCurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _accessor;
    public HttpContextCurrentUserService(IHttpContextAccessor accessor)
        => _accessor = accessor;

    public string? UserName => _accessor.HttpContext?.User?.Identity?.Name;
}

// Background job / console app example
public class SystemUserService : ICurrentUserService
{
    public string? UserName => "system";
}
```

---

## Notes

- **EF version**: Targets EF Core 7+. The `HasMany(Type, string)` overload used for non-generic relationship configuration requires EF Core 6 or later.
- **Scalar type detection**: The column naming pass recognises `string`, `DateTime`, `DateTimeOffset`, `decimal`, `Guid`, `bool`, and the primitive numeric types, plus their nullable variants. Custom value objects require explicit `IEntityTypeConfiguration<T>` classes alongside the conventions.
- **Advanced pluralization**: The built-in `PluralizedNamingConvention` covers basic English rules. For irregular nouns or non-English domains, implement `IEntityNamingConvention` and use [Humanizer](https://github.com/Humanizr/Humanizer) (`"category".Pluralize()`).
- **Per-entity overrides**: Call `modelBuilder.Entity<T>()` after `_conventions.Apply(modelBuilder)` in `OnModelCreating` to override any convention-generated mapping for a specific entity.
