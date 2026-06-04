# AI Implementation Assistant and Prompt Generation

This page provides ready-to-use prompts for three common scenarios. Each prompt is designed to give an AI assistant (Claude, ChatGPT, Copilot etc.) the context it needs to produce accurate, convention-correct EFConventionBuilder code without you having to explain the library from scratch.

---

## Before you start — give the AI context

An AI assistant that knows nothing about EFConventionBuilder will produce generic EF Core code that ignores the library's conventions. Before using any prompt below, paste the README into your chat first:

1. Go to the [EFConventionBuilder README](https://github.com/mtnman1010/EFConventionBuilder/blob/main/README.md)
2. Copy the entire contents
3. Paste it into a new AI chat with this opening line:

```
Here is the documentation for EFConventionBuilder, a convention-over-configuration 
library for EF Core. Please read it carefully — I will ask you to write code using 
this library and I need you to follow its conventions exactly.
```

Wait for the AI to acknowledge it has read and understood the documentation before proceeding with one of the prompts below.

---

## What the README covers

The README gives the AI everything it needs to know about:

- `IEntityBase`, `IEntity`, `IAuditable`, `ISoftDelete`, `ICurrentUserService` — interfaces and their purpose
- `EntityConventionBuilder` — fluent configuration facade and all builder methods
- `IUnitOfWork` / `UnitOfWork` — data access pattern, all available methods
- `ServiceBase<TEntity>` — delete/restore/purge helpers
- `AuditInterceptor` — how audit stamping works
- Naming conventions — PascalCase, snake_case, pluralized, custom
- Relationship detection — nullable reference type convention, StartsWith disambiguation
- Soft delete — global query filter, lifecycle
- All valid configuration patterns
- ICurrentUserService implementations for every hosting environment

---

## Scenario 1 — New project scaffold

Use this when starting a greenfield project and you want the AI to scaffold the full implementation from scratch.

```
Using the EFConventionBuilder documentation I provided, please scaffold a complete 
implementation for my project.

## Project type
[e.g. ASP.NET Core Web API / WPF desktop app / console app / Blazor Server]

## Database and naming convention
Database: [SQL Server / PostgreSQL / SQLite]
Naming convention: [PascalCase (default) / snake_case / pluralized]

## Audit and soft delete
[Choose one:
  - Full audit and soft delete: .WithFullAudit()
  - Soft delete only: .WithSoftDelete()
  - Audit only: .WithAuditFields()
  - Neither — no ICurrentUserService needed
]

## Domain entities
[Describe your entities, their relationships, and which implement IAuditable / ISoftDelete.
Be explicit about required vs optional relationships.

Example:
  Property : IEntity, IAuditable, ISoftDelete
    - has one Address (required — non-nullable)
    - has many Units

  Unit : IEntity, IAuditable
    - belongs to Property (required — non-nullable)
    - has many Leases

  Tenant : IEntity, IAuditable
    - has one Address (required — non-nullable)
    - has many Leases

  Lease : IEntity, IAuditable, ISoftDelete
    - belongs to Unit (required — non-nullable)
    - belongs to Tenant (required — non-nullable)
    - StartDate, EndDate, MonthlyRent [Precision(18,2)]
]

## ICurrentUserService
[How your application resolves the current user. Examples:
  - ASP.NET Core web app: IHttpContextAccessor
  - JWT Bearer token: Claims principal, NameIdentifier claim
  - WPF / WinForms: Windows authentication
  - Background job / console: always "system"
  - No audit stamping: omit entirely
]

## Services needed
[List your services and their key operations. Example:
  IPropertyService — GetProperty, AddProperty, UpdateProperty, DeleteProperty, RestoreProperty
  IUnitService — GetUnit, GetUnitsByProperty, AddUnit
  ILeaseService — GetLease, GetLeasesByUnit, GetLeasesByTenant, AddLease, TerminateLease
]

## Please generate
1. Domain entity classes following EFConventionBuilder v2.3 conventions
2. ICurrentUserService implementation for my hosting environment
3. Concrete UnitOfWork subclass
4. Service interfaces and implementations inheriting ServiceBase<TEntity>
5. DI registration
6. Test infrastructure — FixedUserService and InMemoryDb with example tests
```

---

## Scenario 2 — Continuing development

Use this when you have an existing EFConventionBuilder implementation and want the AI to help extend it — adding entities, services, features, or fixing issues.

```
I am continuing development on an existing EFConventionBuilder v2.3 implementation. 
Using the documentation I provided, please help me with the following.

## My current setup
Naming convention: [PascalCase / snake_case]
Builder configuration: [e.g. .ForAssemblyOf<Customer>().UseSnakeCase().WithFullAudit()]
Database: [SQL Server / PostgreSQL]
Project structure:
  [YourApp].Domain    — entities
  [YourApp].Data      — UnitOfWork subclass, ICurrentUserService implementation
  [YourApp].Services  — application services
  [YourApp].Tests     — xUnit tests

## Existing entities (brief summary)
[List your current entities and key relationships. Example:
  Customer : IEntity, IAuditable — has one Address, has many Orders
  Order : IEntity, IAuditable, ISoftDelete — belongs to Customer, has many OrderItems
  Product : IEntity, IAuditable, ISoftDelete — belongs to Category
]

## What I need
[Be specific about what you want to add or change. Examples:
  - "Add a Supplier entity with a required Address FK and a one-to-many 
    relationship with Product. Supplier needs IAuditable and ISoftDelete."
  - "Add ISupplierService with GetSupplier, AddSupplier, DeleteSupplier, 
    RestoreSupplier, and GetProductsBySupplier."
  - "Add unit and integration tests for SupplierService covering add, 
    soft delete, restore, and purge."
  - "Add a GetOrdersByDateRangeAsync method to IOrderService that filters 
    by OrderDate and includes Customer and Items."
]
```

---

## Scenario 3 — Porting an existing EF Core project

Use this when you have an existing EF Core application — whether using EF6, EF Core with explicit `OnModelCreating` configuration, or a repository pattern — and you want to migrate it to EFConventionBuilder.

Porting is not a rewrite. The goal is incremental migration — adopt the conventions gradually without breaking your existing application.

```
I have an existing EF Core application that I want to migrate to EFConventionBuilder v2.3.
Using the documentation I provided, please help me plan and execute this migration.

## My current setup
EF version: [EF6 / EF Core X.X]
Current pattern: [explicit OnModelCreating / repository pattern / DbContext directly / other]
Database: [SQL Server / PostgreSQL]
Existing naming: [snake_case / PascalCase / mixed / legacy column names]
Scalar FK properties: [yes — CustomerId, AddressId etc. / no]
Existing audit: [manual in services / none / different column names]
Existing soft delete: [IsDeleted column / archive table / none]

## My existing domain (paste your current entities)
[Paste your actual entity classes here — the AI needs to see the exact 
property names, types, relationships, and any Data Annotations]

## My existing DbContext (paste OnModelCreating)
[Paste your current DbContext or repository configuration so the AI can 
see what explicit mappings exist and what the conventions need to replace]

## Migration goals
[What do you want to achieve? Examples:
  - "Adopt EFConventionBuilder conventions for all new entities going forward, 
    leave existing entities unchanged for now"
  - "Migrate all entities to remove scalar FK properties and use nullable 
    reference type convention"
  - "Add IAuditable and ISoftDelete to existing entities without changing 
    the database schema — use column name overrides to match existing columns"
  - "Replace our existing Repository<T> pattern with IUnitOfWork and ServiceBase"
  - "Full migration — adopt all EFConventionBuilder conventions and update 
    the database schema"
]

## Schema constraints
[Are there existing column names you must keep? Examples:
  - "Audit columns are currently named created_date and modified_date — 
    these match EFConventionBuilder defaults, no override needed"
  - "Audit columns are named RecordCreatedDate and RecordCreatedBy — 
    I need to override the defaults"
  - "FK columns are currently named customer_id — I need to keep them 
    for now and migrate to the navigation name convention later"
  - "No schema constraints — I can run a migration to update column names"
]

## Please help me with
[Choose what you need:
  - Migration plan — step by step approach for incremental adoption
  - Updated entity classes — refactored to EFConventionBuilder conventions
  - UnitOfWork subclass — replacing the existing DbContext configuration
  - Column name overrides — keeping legacy column names during transition
  - ServiceBase migration — replacing existing repository/service pattern
  - Test infrastructure — FixedUserService and InMemoryDb for the migrated code
]
```

### Porting tips

**Start with new entities** — adopt EFConventionBuilder conventions on all new entities from day one. Leave existing entities as-is initially. This gives you immediate value without risk.

**Use column name overrides for legacy schemas** — if your existing database has audit columns named `created_at` and `modified_at`, override the defaults rather than running a schema migration:

```csharp
.WithAuditFields(cols =>
{
    cols.CreatedDate  = "created_at";   // keep existing column name
    cols.ModifiedDate = "modified_at";
})
```

**Keep scalar FK properties during transition** — removing `CustomerId` from every entity at once is risky. Keep them while migrating — they're optional in v2.3 but still fully supported. Remove them gradually once the migration is stable.

**Migrate OnModelCreating incrementally** — move one entity's configuration to conventions at a time. Verify with tests after each entity. Don't attempt a big-bang migration.

**Run tests after every entity** — EFConventionBuilder's startup validation catches most configuration errors immediately. If something breaks the error message tells you exactly what and where.

---

## Tips for better results

**Paste entity code directly** — instead of describing your entities in prose, paste the actual C# classes. The AI produces more accurate output when it can see exact property names, types, and nullability annotations.

**One scenario at a time** — scaffold the domain first, review it, then ask for services. Smaller focused requests produce cleaner output.

**Reference the Wiki for specific topics** — if the AI produces code that doesn't match EFConventionBuilder conventions, point it to a specific page: "The FK column naming is wrong — read the [[Naming Conventions]] page and fix it."

**Iterate** — use the generated code as a starting point. Follow up with targeted requests: "Add a GetDeletedOrdersAsync method to IOrderService" or "Update OrderService to validate that all products are active before placing an order."

**For porting** — give the AI your actual existing code, not a description of it. The more context it has about what currently exists, the more accurate the migration plan will be.
