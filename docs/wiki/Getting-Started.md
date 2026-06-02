# Getting Started

This page walks you through adding EFConventionBuilder to a new project from scratch. By the end you will have a working `DbContext`, a domain entity, and a service — all wired together with convention-based configuration.

---

## Prerequisites

- .NET 8, 9, or 10
- EF Core 8+ (pulled in automatically as a dependency)
- A SQL Server, PostgreSQL, SQLite, or other relational database provider of your choice

---

## Step 1 — Install the package

```bash
dotnet add package EFConventionBuilder
```

Or search for `EFConventionBuilder` in the Visual Studio NuGet Package Manager.

---

## Step 2 — Define your domain entities

Implement `IEntity` on each domain class. The convention builder discovers all classes implementing `IEntityBase` or `IEntity` in your domain assembly automatically — no `[Table]`, `[Column]`, or `DbSet<T>` declarations required.

```csharp
using EFConvention;
using Microsoft.EntityFrameworkCore;

// Plain entity — no audit, no soft delete
public class Category : IEntity
{
    public int    Id          { get; set; }
    public string Name        { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    // Private setter — enforced by startup validation
    public ICollection<Product> Products { get; private set; } = new List<Product>();
}

// Full audit + soft delete
public class Product : IEntity, IAuditable, ISoftDelete
{
    public int    Id          { get; set; }
    public string Name        { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    // [Precision] picked up automatically
    [Precision(18, 2)]
    public decimal Price { get; set; }

    // Non-nullable → required FK, DB column: "Category"
    // No scalar FK property (CategoryId) needed — v2.3 detects required/optional
    // from the nullable reference type annotation on the navigation property
    public Category Category { get; set; } = null!;

    // IAuditable — stamped by AuditInterceptor, never set manually
    public DateTime  CreatedDate  { get; set; }
    public string    CreatedBy    { get; set; } = string.Empty;
    public DateTime? ModifiedDate { get; set; }
    public string?   ModifiedBy   { get; set; }

    // ISoftDelete — managed by ServiceBase.DeleteAsync
    public bool      IsDeleted   { get; set; }
    public DateTime? DeletedDate { get; set; }
    public string?   DeletedBy   { get; set; }
}
```

**Rules enforced at startup:**
- Collection navigation properties must use `private set;` or no setter — a public setter causes a startup error
- All discovered classes must implement `IEntityBase` or `IEntity` — interfaces or abstract classes are ignored

**Nullable reference types:**
Required/optional FK detection in v2.3 uses nullable reference type annotations. Ensure your project has `<Nullable>enable</Nullable>` in the `.csproj` file — without it all navigations are treated as optional.

---

## Step 3 — Implement ICurrentUserService

`ICurrentUserService` is only required when using audit stamping (`WithAuditFields()` or `WithFullAudit()`). If you don't need audit stamping skip this step.

The interface is defined in the library — you provide the implementation suited to your hosting environment:

```csharp
// ASP.NET Core web app
public class HttpContextCurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _accessor;
    public HttpContextCurrentUserService(IHttpContextAccessor accessor)
        => _accessor = accessor;
    public string? UserName => _accessor.HttpContext?.User?.Identity?.Name;
}

// Background job or console app
public class SystemUserService : ICurrentUserService
{
    public string? UserName => "system";
}
```

See [[Audit Stamping]] for implementations covering WPF, Blazor, JWT, Windows auth, and more.

---

## Step 4 — Subclass UnitOfWork

Create one concrete `UnitOfWork` subclass per database. This is the only place that knows your connection string, domain assembly, and convention configuration.

```csharp
using EFConvention;
using Microsoft.EntityFrameworkCore;

public sealed class AppDb : UnitOfWork
{
    private readonly string _connectionString;
    private readonly AuditInterceptor _auditInterceptor;

    public AppDb(string connectionString, ICurrentUserService currentUser)
        : base(
            domainAssembly:       typeof(Product).Assembly,
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
```

**Convention options:**

```csharp
// PascalCase (default) — table and column names match C# names exactly
b => b.WithFullAudit()

// snake_case — recommended for PostgreSQL
b => b.UseSnakeCase().WithFullAudit()

// Pluralized table names
b => b.UsePluralizedTables().WithFullAudit()

// No audit stamping needed
b => b.UseSnakeCase()
```

**Important:** Always call `base.OnModelCreating(modelBuilder)` before `_conventions.Apply(modelBuilder)` inside `OnModelCreating` if you override it. The `UnitOfWork` base class handles this correctly — only override if you need to add extra configuration after the conventions are applied.

---

## Step 5 — Write application services

Inherit from `ServiceBase<TEntity>` to get delete, restore, and purge helpers for free:

```csharp
public interface IProductService
{
    Task<Product?> GetProductAsync(int id, CancellationToken ct = default);
    Task<Product> AddProductAsync(Product product, CancellationToken ct = default);
    Task DeleteProductAsync(int id, CancellationToken ct = default);
}

public class ProductService : ServiceBase<Product>, IProductService
{
    public ProductService(IUnitOfWork uow, ICurrentUserService user)
        : base(uow, user) { }

    public async Task<Product?> GetProductAsync(int id, CancellationToken ct = default) =>
        await UnitOfWork.Query<Product>()
            .Include(p => p.Category)
            .FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<Product> AddProductAsync(Product product, CancellationToken ct = default)
    {
        if (product.Price <= 0)
            throw new ArgumentException("Price must be greater than zero.", nameof(product));

        UnitOfWork.Add(product);
        await UnitOfWork.CompleteAsync(ct);
        return product;
    }

    public async Task DeleteProductAsync(int id, CancellationToken ct = default)
    {
        var product = await UnitOfWork.Query<Product>()
            .FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new KeyNotFoundException($"Product {id} not found.");

        // Product implements ISoftDelete → soft delete chosen automatically
        // Remove ISoftDelete from Product → this becomes a hard delete
        await DeleteAsync(product, ct);
    }
}
```

---

## Step 6 — Register with DI

### ASP.NET Core (Program.cs)

```csharp
var builder = WebApplication.CreateBuilder(args);

// ICurrentUserService — resolves identity from HTTP request
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, HttpContextCurrentUserService>();

// IUnitOfWork — backed by your concrete DbContext
builder.Services.AddScoped<IUnitOfWork>(sp =>
    new AppDb(
        builder.Configuration.GetConnectionString("AppDb")!,
        sp.GetRequiredService<ICurrentUserService>()));

// Application services
builder.Services.AddScoped<IProductService, ProductService>();

var app = builder.Build();
```

### Background job or console app

```csharp
var services = new ServiceCollection();

services.AddScoped<ICurrentUserService, SystemUserService>();

services.AddScoped<IUnitOfWork>(sp =>
    new AppDb(
        connectionString,
        sp.GetRequiredService<ICurrentUserService>()));

services.AddScoped<IProductService, ProductService>();
```

---

## Step 7 — Run migrations or create the schema

EFConventionBuilder works with EF Core migrations as normal. From the terminal:

```bash
dotnet ef migrations add InitialCreate --project YourApp
dotnet ef database update
```

Or generate a SQL script instead:

```bash
dotnet ef migrations script --output schema.sql
```

The generated SQL will use the naming convention you configured — PascalCase, snake_case, or pluralized.

---

## What you get automatically

Once wired up, every save through `CompleteAsync` automatically:

- Stamps `CreatedDate` and `CreatedBy` on INSERT for all `IAuditable` entities
- Stamps `ModifiedDate` and `ModifiedBy` on UPDATE for all `IAuditable` entities
- Applies `WHERE IsDeleted = 0` to every query for all `ISoftDelete` entities
- Validates decimal precision via `[Precision]` attributes

And `ServiceBase<TEntity>` gives every service:

- `DeleteAsync` — soft delete if `ISoftDelete`, hard delete if not, chosen at runtime
- `RestoreAsync` — clears soft delete fields, row becomes visible again
- `PurgeAsync` — permanent physical delete, requires prior soft delete

---

## Next steps

- [[Naming Conventions]] — full reference for PascalCase, snake_case, pluralized, and custom strategies
- [[Relationships]] — how required/optional FK detection works, multiple collections of the same type
- [[Audit Stamping]] — `ICurrentUserService` implementations for every hosting environment
- [[Soft Delete]] — the full delete/restore/purge lifecycle
- [[Testing]] — unit testing services with Moq, integration testing with the in-memory provider
