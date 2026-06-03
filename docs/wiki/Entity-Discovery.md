# Entity Discovery

EFConventionBuilder discovers your domain entities automatically by scanning an assembly for classes implementing `IEntityBase` or `IEntity`. No `DbSet<T>` properties, `[Table]` attributes, or explicit registration required.

---

## How discovery works

At startup, `EntityConventionBuilder.Apply()` scans the target assembly and registers every class that:

- Is a **concrete class** (not abstract, not an interface)
- Implements `IEntityBase` or `IEntity` (directly or via `IEntity`)

Every discovered type is registered as an EF Core entity with a table name, column mappings, and FK relationships applied automatically via the active naming convention.

---

## The interfaces

### `IEntityBase`

Root discovery marker. Any class implementing `IEntityBase` is discovered and registered as an EF Core entity.

```csharp
public interface IEntityBase { }
```

Use `IEntityBase` directly when your entity has a non-integer primary key or a composite key that you configure manually:

```csharp
public class AuditLog : IEntityBase
{
    public Guid   Id        { get; set; }  // non-integer PK
    public string EventType { get; set; } = string.Empty;
    public string Details   { get; set; } = string.Empty;
}
```

### `IEntity`

Extends `IEntityBase` with a typed integer primary key. Most domain entities implement this directly.

```csharp
public interface IEntity : IEntityBase
{
    int Id { get; set; }
}
```

```csharp
public class Customer : IEntity
{
    public int    Id    { get; set; }
    public string Name  { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}
```

---

## Why interfaces instead of a base class

Earlier versions of this pattern used an abstract `Entity` base class. The interface approach has two significant advantages:

**No forced inheritance** — a class that already inherits from a third-party base type (a ViewModel base, a framework class, etc.) can still participate in discovery by implementing `IEntity` directly. Abstract base class inheritance would prevent this.

**No infrastructure noise** — domain objects don't carry any EF Core concerns in their inheritance chain. They're plain C# classes that happen to implement a marker interface.

### Optional convenience base class

If you find yourself repeating `public int Id { get; set; }` on every entity, you can define a shared base class in your own application that implements `IEntity`:

```csharp
// In your application — not in the library
public abstract class EntityBase : IEntity
{
    public int Id { get; set; }
}

// Domain objects inherit the Id automatically
public class Product : EntityBase, IAuditable, ISoftDelete
{
    public string   Name     { get; set; } = string.Empty;
    public Category Category { get; set; } = null!;

    public DateTime  CreatedDate  { get; set; }
    public string    CreatedBy    { get; set; } = string.Empty;
    public DateTime? ModifiedDate { get; set; }
    public string?   ModifiedBy   { get; set; }

    public bool      IsDeleted   { get; set; }
    public DateTime? DeletedDate { get; set; }
    public string?   DeletedBy   { get; set; }
}
```

This is a suggested pattern — not required by the library. Use it when it makes your domain cleaner. Avoid it when your classes need to inherit from something else.

---

## Factory methods

Three ways to tell the builder which assembly or types to scan:

### `ForAssemblyOf<T>()`

Scans the assembly containing `T`. The most common pattern — pick any stable domain type as the anchor:

```csharp
EntityConventionBuilder.ForAssemblyOf<Customer>()
```

`Customer` is just an anchor — any class in your domain assembly works. The builder discovers all `IEntityBase` implementations in that assembly, not just `Customer`.

### `ForAssembly(Assembly)`

Scans a specific assembly explicitly. Useful when the anchor type isn't convenient to reference directly:

```csharp
EntityConventionBuilder.ForAssembly(typeof(Customer).Assembly)
```

### `ForTypes(params Type[])`

Registers only the specified types. No assembly scanning. Useful in two scenarios:

**Testing** — register only the types needed for a specific test without picking up unrelated test fixtures:

```csharp
EntityConventionBuilder
    .ForTypes(typeof(Principal), typeof(Dependent))
    .UseSnakeCase()
    .Apply(modelBuilder);
```

**Bounded contexts** — when multiple bounded contexts share an assembly, register only the types belonging to each context:

```csharp
// Sales context
EntityConventionBuilder.ForTypes(
    typeof(Customer), typeof(Order), typeof(OrderItem));

// Inventory context
EntityConventionBuilder.ForTypes(
    typeof(Product), typeof(Category), typeof(Supplier));
```

---

## What gets discovered — and what doesn't

| Type | Discovered? | Reason |
|---|---|---|
| `class Customer : IEntity` | ✅ Yes | Concrete class implementing IEntity |
| `class Product : IEntity, IAuditable` | ✅ Yes | Concrete class, multiple interfaces |
| `abstract class EntityBase : IEntity` | ❌ No | Abstract — skipped |
| `interface ISpecialEntity : IEntity` | ❌ No | Interface — skipped |
| `class AuditLog : IEntityBase` | ✅ Yes | IEntityBase is the root marker |
| `class OrderService` | ❌ No | Does not implement IEntityBase |
| `record Point(int X, int Y)` | ❌ No | Does not implement IEntityBase |

---

## Startup validation

After discovery, the builder validates each entity before applying configuration. All errors are collected and thrown together as a single `InvalidOperationException` — you see every problem at once rather than fixing them one at a time:

```
EFConvention — 2 configuration error(s) detected:
  1. Customer.Orders has a public setter. Collection navigation properties
     must use 'private set;' or no setter to prevent external replacement
     of the collection.
  2. Product.Variants must be virtual. Lazy loading proxies require virtual
     navigation properties.
```

**Validated automatically:**

- **Collection setters** — every `ICollection<T>`, `List<T>`, or `IList<T>` navigation property must have `private set;` or no setter. A public setter allows external code to replace the entire collection, breaking aggregate encapsulation.
- **Virtual navigations** — when `WithLazyLoadingValidation()` is enabled, all navigation properties must be declared `virtual` for EF Core's lazy loading proxy generator to work correctly.

---

## Nullable reference types and discovery

Discovery itself is not affected by nullable reference types — all concrete `IEntityBase` classes are discovered regardless. However nullable annotations on **navigation properties** do affect how the builder determines required vs optional relationships:

```csharp
public class Order : IEntity
{
    public int      Id       { get; set; }

    // Non-nullable → required FK
    public Customer Customer { get; set; } = null!;

    // Nullable → optional FK
    public Coupon?  Coupon   { get; set; }
}
```

For this to work correctly your project must have `<Nullable>enable</Nullable>` in its `.csproj`. Without it the compiler treats all reference types as nullable and the builder cannot distinguish required from optional navigations.

See [[Relationships]] for the full required/optional detection rules.

---

## Next steps

- [[Naming Conventions]] — how discovered types are mapped to table and column names
- [[Relationships]] — how navigation properties are wired into FK relationships
- [[Getting Started]] — complete walkthrough from install to running application
