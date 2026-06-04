# EFConventionBuilder

**Convention-over-configuration for EF Core.** Drop it into any project to automatically discover domain entities, map table and column names, wire foreign key relationships, enforce soft delete, and stamp audit fields — all without writing repetitive `OnModelCreating` boilerplate.

---

## What it does

EFConventionBuilder scans your domain assembly at startup and automatically:

- **Discovers** all classes implementing `IEntityBase` or `IEntity` and registers them as EF Core entities
- **Maps table names** using your chosen naming convention — PascalCase (default), snake_case, or pluralized
- **Maps column names** consistently across every entity
- **Wires FK relationships** from navigation property types, detecting required vs optional automatically
- **Resolves multiple collections** of the same type using a `StartsWith` name convention — no attributes needed
- **Applies `[Precision]`** attributes on decimal properties automatically
- **Registers global query filters** for soft delete (`WHERE IsDeleted = 0`)
- **Stamps audit fields** via `AuditInterceptor` on every save — no `SaveChanges` override needed
- **Validates** collection setter accessibility and lazy loading virtuality at startup, reporting all errors together

---

## Install

```bash
dotnet add package EFConventionBuilder
```

Or search for `EFConventionBuilder` in the Visual Studio NuGet Package Manager.

---

## Minimum example

```csharp
// 1. Domain entity — no scalar FK properties needed in v2.3
public class Product : IEntity, IAuditable, ISoftDelete
{
    public int    Id          { get; set; }
    public string Name        { get; set; } = string.Empty;

    [Precision(18, 2)]
    public decimal Price { get; set; }

    // Non-nullable → required FK, DB column: "Category"
    public Category Category { get; set; } = null!;

    // Private setter — enforced by startup validation
    public ICollection<OrderItem> OrderItems
        { get; private set; } = new List<OrderItem>();

    public DateTime  CreatedDate  { get; set; }
    public string    CreatedBy    { get; set; } = string.Empty;
    public DateTime? ModifiedDate { get; set; }
    public string?   ModifiedBy   { get; set; }

    public bool      IsDeleted   { get; set; }
    public DateTime? DeletedDate { get; set; }
    public string?   DeletedBy   { get; set; }
}

// 2. DbContext
public sealed class AppDb : UnitOfWork
{
    private readonly string _cs;
    private readonly AuditInterceptor _audit;

    public AppDb(string connectionString, ICurrentUserService user)
        : base(typeof(Product).Assembly, b => b.WithFullAudit())
    {
        _cs    = connectionString;
        _audit = new AuditInterceptor(user);
    }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        if (!options.IsConfigured)
            options.UseSqlServer(_cs).AddInterceptors(_audit);
    }
}

// 3. Service
public class ProductService : ServiceBase<Product>
{
    public ProductService(IUnitOfWork uow, ICurrentUserService user)
        : base(uow, user) { }

    public async Task DeleteProductAsync(int id, CancellationToken ct = default)
    {
        var product = await UnitOfWork.Query<Product>()
            .FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new KeyNotFoundException($"Product {id} not found.");

        await DeleteAsync(product, ct);  // soft delete — Product implements ISoftDelete
    }
}
```

---

## Version history

| Version | Date | Highlights |
|---|---|---|
| **2.3.0** | 2026 | Nullable reference type convention — scalar FK properties optional. Non-nullable navigation → required, nullable navigation → optional. Dead code cleanup. |
| **2.2.0** | 2026 | FK columns named after navigation property (`Customer` not `CustomerId`). `GetCollection` disambiguation for multiple collections of same type. `IAuditable` property renames (`CreatedDate`, `ModifiedDate`). `ForTypes` factory. |
| **2.1.0** | 2026 | `ICurrentUserService` moved into library. `ServiceBase<T>` ships in library. `AuditInterceptor` decoupled from `UnitOfWork`. `[Obsolete]` on sync `Complete()`. |

---

## Key concepts

| Concept | Description | Wiki page |
|---|---|---|
| Entity discovery | How `IEntityBase` and `IEntity` work | [[Entity Discovery]] |
| Naming conventions | PascalCase, snake_case, pluralized, custom | [[Naming Conventions]] |
| Relationships | Required/optional via nullable reference types, multiple collections | [[Relationships]] |
| Audit stamping | `IAuditable`, `AuditInterceptor`, `ICurrentUserService` | [[Audit Stamping]] |
| Soft delete | `ISoftDelete`, global filter, lifecycle | [[Soft Delete]] |
| Unit of Work | `IUnitOfWork`, async patterns | [[Unit of Work]] |
| Service Base | Delete/restore/purge, multiple entities | [[Service Base]] |
| Testing | `ForTypes`, in-memory, Moq | [[Testing]] |
| Migration guide | v2.1 → v2.3 breaking changes | [[Migration Guide]] |
| FAQ | Common questions | [[FAQ]] |
| AI assistant | Prompt generator for new implementations | [[AI Implementation Assistant]] |

---

## Library boundary

EFConventionBuilder is split into two clear layers:

**Library** (ships in the NuGet package — never modify):
```
IEntityBase, IEntity          — discovery interfaces
IAuditable, ISoftDelete        — behavioural contracts
ICurrentUserService            — identity abstraction
EntityConventionBuilder        — the convention facade
IUnitOfWork, UnitOfWork        — data access pattern
AuditInterceptor               — audit stamping
ServiceBase<TEntity>           — delete/restore/purge helpers
```

**Application** (you write this once per project):
```
Domain entities                — implement IEntity, IAuditable, ISoftDelete
ICurrentUserService impl       — HttpContext, Windows auth, session, etc.
Concrete UnitOfWork subclass   — your DbContext with connection string
Application services           — inherit ServiceBase<T>
```

---

## Supported hosting environments

`ICurrentUserService` is the only interface you must implement when using audit stamping. It works in any hosting environment:

| Environment | Implementation approach |
|---|---|
| ASP.NET Core MVC / API | `IHttpContextAccessor` → `User.Identity.Name` |
| Web API with JWT | Claims principal → `NameIdentifier` or `sub` claim |
| Blazor Server | `AuthenticationStateProvider` |
| WPF / WinForms | Windows auth or custom session service |
| Background jobs | `SystemUserService` returning `"system"` |
| Console / CLI | `Environment.UserName` or prompt at startup |
| Unit tests | `FixedUserService` with a known username |
| No audit needed | Don't call `WithAuditFields()` — never needed |

See [[Audit Stamping]] for full implementation examples for each environment.

---

## Links

- **NuGet**: [nuget.org/packages/EFConventionBuilder](https://nuget.org/packages/EFConventionBuilder)
- **Source**: [github.com/mtnman1010/EFConventionBuilder](https://github.com/mtnman1010/EFConventionBuilder)
- **Issues**: [github.com/mtnman1010/EFConventionBuilder/issues](https://github.com/mtnman1010/EFConventionBuilder/issues)
- **License**: MIT
