# Roadmap

This page outlines the planned direction for EFConventionBuilder. Items marked as **planned** are committed for the next release. Items marked as **under consideration** are on the radar but not yet committed. All timelines are best-effort — this is open source.

If you have a feature request or want to contribute, open an issue on [GitHub](https://github.com/mtnman1010/EFConventionBuilder/issues).

---

## v2.4 — Planned

The focus of v2.4 is completing the EF Core facade so EFConventionBuilder covers the full spectrum of data access scenarios without consumers ever needing to reach around the library.

### `Find<T>(id)` — PK anchor with fluent composition

A new `Find<T>(id)` method on `IUnitOfWork` returning `IQueryable<T>` pre-filtered by primary key. Complements the existing `FindAsync<T>(id)` with a composable alternative:

```csharp
// Simple PK lookup — use FindAsync (identity map benefit)
var customer = await UnitOfWork.FindAsync<Customer>(id, ct);

// PK lookup with includes — use Find
var order = await UnitOfWork.Find<Order>(id)
    .Include(o => o.Customer)
    .Include(o => o.Items)
    .FirstOrDefaultAsync(ct);

// PK lookup bypassing soft delete filter
var order = await UnitOfWork.Find<Order>(id)
    .IgnoreQueryFilters()
    .FirstOrDefaultAsync(ct);
```

The three-method vocabulary becomes: `FindAsync` for simple PK lookup, `Find` for PK with composition, `Query` for criteria-based search.

### `IUnitOfWorkFactory` — factory lifetime for desktop and console apps

A new `IUnitOfWorkFactory` interface and `UnitOfWorkFactory<TContext>` implementation backed by EF Core's `IDbContextFactory`. Provides per-operation `DbContext` lifetime for desktop, WPF, WinForms, and console applications where a scoped-per-request lifetime doesn't apply.

Consumers depend on `IUnitOfWorkFactory` instead of `IUnitOfWork` when per-operation lifetime is needed:

```csharp
public class CustomerService
{
    private readonly IUnitOfWorkFactory _factory;

    public CustomerService(IUnitOfWorkFactory factory)
        => _factory = factory;

    public async Task SaveCustomerAsync(Customer customer, CancellationToken ct)
    {
        await using var uow = await _factory.CreateAsync(ct);
        uow.Add(customer);
        await uow.CompleteAsync(ct);
    }
}
```

DI extension methods for both patterns:

```csharp
// Web — scoped instance per request
services.AddUnitOfWork<AppDb>(connectionString);

// Desktop / console — factory, new instance per operation
services.AddUnitOfWorkFactory<AppDb>(connectionString);
```

### Explicit transaction support

A `BeginTransactionAsync` method on `IUnitOfWork` for multi-step operations that must commit or roll back together:

```csharp
await using var tx = await UnitOfWork.BeginTransactionAsync(ct);
try
{
    UnitOfWork.Add(order);
    await UnitOfWork.CompleteAsync(ct);

    UnitOfWork.Update(inventory);
    await UnitOfWork.CompleteAsync(ct);

    await tx.CommitAsync(ct);
}
catch
{
    await tx.RollbackAsync(ct);
}
```

### Bulk operations facade

Expose EF Core 7+ `ExecuteDeleteAsync` and `ExecuteUpdateAsync` through `IUnitOfWork` for set-based operations that bypass the change tracker:

```csharp
// Bulk delete — no entity loading needed
await UnitOfWork.ExecuteDeleteAsync<Order>(o => o.IsDeleted && o.DeletedDate < cutoff, ct);

// Bulk update
await UnitOfWork.ExecuteUpdateAsync<Product>(
    p => p.Category.Id == categoryId,
    s => s.SetProperty(p => p.IsDeleted, true),
    ct);
```

### `ExecuteNonQueryAsync` — raw SQL and stored procedures

```csharp
await UnitOfWork.ExecuteNonQueryAsync(
    "EXEC sp_ArchiveOrders @cutoffDate",
    new { cutoffDate = DateTime.UtcNow.AddYears(-1) },
    ct);
```

### Composite key support

Support for multi-column primary keys. Initial scope covers leaf node entities — those with no outbound FK navigations pointing to them. Junction/join tables are the most common use case:

```csharp
public class StudentCourse : IEntityBase
{
    public int StudentId { get; set; }
    public int CourseId  { get; set; }

    public Student Student { get; set; } = null!;
    public Course  Course  { get; set; } = null!;
}
```

### CI workflow

GitHub Actions workflow triggering on pull requests and branch pushes — build and test without publish. Added as a required status check on `main` so nothing broken can merge.

---

## v2.4 — Documentation

- Unit of Work wiki updated with `# UnitOfWork Design Pattern` heading, lifetime scope guidance, factory pattern, transaction pattern, and bulk operations
- Compiled queries documented — EF Core compiled queries work naturally through `Query<T>()`, no library changes needed
- CHANGELOG.md added to repository root
- NuGet description updated to reflect complete facade scope

---

## Under consideration

These items are on the radar but not yet committed to a specific version. Community feedback and adoption patterns will influence prioritisation.

### Value converters — convention-based detection

Convention-based value converter registration. A `[StoreAsString]`, `[StoreAsJson]`, or custom attribute the builder picks up automatically — similar to how `[Precision]` works today. Would eliminate per-entity `modelBuilder.Entity<T>()` overrides for common conversion scenarios.

Currently works via manual override after `Apply()` — see the [[FAQ]] for details.

### `EFConvention.Domain` — optional companion package

A separate NuGet package (`EFConvention.Domain`) providing pre-built domain building blocks as interfaces with automatic convention wiring:

- `IHasNotes` — linked note entities
- `IHasTags` — flexible tagging
- `IHasAttachments` — file reference linking
- `IHasAddress` — embedded address convention

Completely separate package, separate versioning, entirely opt-in. Not part of the core library. Build when adoption patterns make the right interfaces clear.

### `ForAssemblyOf<T>().Exclude<T>()` — assembly scan exclusions

Exclude specific types from assembly scanning without switching to `ForTypes()`:

```csharp
EntityConventionBuilder
    .ForAssemblyOf<Customer>()
    .Exclude<LegacyEntity>()
    .Exclude<ThirdPartyEntity>()
    .WithFullAudit();
```

### Diagnostic output — `.Describe()`

A `.Describe()` or `.Validate()` method on `EntityConventionBuilder` returning a human-readable summary of all discovered entities, their tables, columns, FK relationships, and applied conventions. Answers the most common adopter question: "why is my column named X instead of Y?"

### Dev.to / Medium articles

Technical articles targeting high-search developer topics:

- "Eliminating EF Core boilerplate with convention-over-configuration"
- "Clean EF Core domain objects with nullable reference types"
- "EF Core soft delete without boilerplate"
- "Unit of work pattern with EF Core in 2026"
- "EF Core for WPF and desktop apps — factory pattern"

Planned after v2.4 when there is richer feature content to write about.

### Submit to awesome-dotnet

Open a PR to [awesome-dotnet](https://github.com/quozd/awesome-dotnet) to add EFConventionBuilder to the ORM / Data Access section.

---

## Completed

### v2.3.0 — 2026

- Nullable reference type convention — scalar FK properties optional
- Non-nullable navigation → required, nullable navigation → optional
- `GetCollectionProperties` and `IsCollectionPropertyOf` dead code removed
- `<Nullable>enable</Nullable>` required for nullable reference type detection
- Updated domain sample, tests, README, Wiki, and reference PDF

### v2.2.0 — 2026

- FK columns named after navigation property (`Customer` not `CustomerId`)
- `GetCollection` disambiguation — multiple collections of same type resolved by `StartsWith` convention
- `IAuditable` property renames: `CreatedAt` → `CreatedDate`, `ModifiedAt` → `ModifiedDate`
- `ISoftDelete` property rename: `DeletedAt` → `DeletedDate`
- Default column name updates to match property names
- `ForTypes(params Type[])` factory method for targeted assembly registration
- GitHub Actions CI/CD pipeline — publish to NuGet on version tag

### v2.1.0 — 2026

- `ICurrentUserService` moved into library — defined once, implemented per application
- `ServiceBase<TEntity>` ships in library — delete/restore/purge helpers
- `AuditInterceptor` decoupled from `UnitOfWork` — registered via `AddInterceptors()`
- `Complete()` marked `[Obsolete]` — use `CompleteAsync()` instead
- `Add`, `Update`, `Remove` use explicit interface implementation on `IUnitOfWork`
- GitHub repository, NuGet package (`EFConventionBuilder`) published

---

## Not planned

These are explicitly out of scope for EFConventionBuilder. Alternatives are noted where applicable.

**Full audit trail with change history** — storing every change to every field over time is a significant feature with its own storage, querying, and performance considerations. Use [Audit.NET](https://github.com/thepirat000/Audit.NET) instead — it is purpose-built for this.

**CQRS / MediatR integration** — EFConventionBuilder targets traditional n-tier architecture. Teams using CQRS, MediatR, or vertical slice architecture are better served by those patterns directly.

**GraphQL / OData support** — out of scope. Both have dedicated libraries (`Hot Chocolate`, `Microsoft.AspNetCore.OData`) that integrate with EF Core directly.

**Multi-tenancy** — tenant isolation, row-level security, and schema-per-tenant patterns are complex and opinionated. Out of scope for the core library.

**Non-relational databases** — EFConventionBuilder targets relational databases (SQL Server, PostgreSQL, SQLite). EF Core providers for Cosmos DB and other non-relational stores have fundamentally different modelling requirements.

---

## Contributing

Have an idea or want to contribute to a planned feature? Open an issue at [github.com/mtnman1010/EFConventionBuilder/issues](https://github.com/mtnman1010/EFConventionBuilder/issues).

Please read `CONTRIBUTING.md` before opening a pull request.
