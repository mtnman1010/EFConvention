# Relationships

EFConventionBuilder wires FK relationships automatically from navigation property types and nullability annotations. No `HasMany`, `HasOne`, `WithMany`, or `HasForeignKey` fluent calls required in most cases.

---

## How relationships are detected

The builder scans reference navigation properties on each entity (properties whose type is another discovered entity type) and configures the relationship using three pieces of information:

1. **The navigation property type** — identifies the principal entity
2. **Required vs optional** — detected via a three-step priority chain
3. **The inverse collection** — found on the principal using `StartsWith` convention

---

## Required vs optional — detection priority

The builder uses the first matching rule in this order:

| Priority | Condition | Result |
|---|---|---|
| 1 | `[Required]` attribute on the navigation | `IsRequired(true)` |
| 2 | Scalar FK property `int` (non-nullable) | `IsRequired(true)` |
| 2 | Scalar FK property `int?` (nullable) | `IsRequired(false)` |
| 3 | Non-nullable navigation `Address Address` | `IsRequired(true)` |
| 3 | Nullable navigation `Address? Address` | `IsRequired(false)` |

### Priority 1 — `[Required]` attribute

Explicitly marks a relationship as required regardless of FK or nullability:

```csharp
[Required]
public Customer Customer { get; set; } = null!;  // required — attribute wins
```

### Priority 2 — scalar FK property nullability

When a scalar FK property exists alongside the navigation, its nullability drives required/optional detection. Backwards-compatible with v2.1 and v2.2 code:

```csharp
// Required — non-nullable int
public int      CustomerId { get; set; }
public Customer Customer   { get; set; } = null!;

// Optional — nullable int?
public int?      CustomerId { get; set; }
public Customer? Customer   { get; set; }
```

Scalar FK properties are still fully supported but no longer required in v2.3.

### Priority 3 — nullable reference type annotation (v2.3)

When no scalar FK property exists, the nullability of the navigation property itself determines required vs optional. This is the cleanest v2.3 approach — no scalar FK, no attribute, just C# type annotations:

```csharp
public class Order : IEntity
{
    public int      Id       { get; set; }

    // Non-nullable → required FK, DB column: "Customer"
    public Customer Customer { get; set; } = null!;

    // Nullable → optional FK, DB column: "Coupon"
    public Coupon?  Coupon   { get; set; }
}
```

**Important:** Requires `<Nullable>enable</Nullable>` in your `.csproj`. Without it the compiler treats all reference types as nullable and the builder cannot distinguish required from optional navigations — all become optional.

---

## FK column naming

FK columns are named after the **navigation property name** — not the principal type name with an `Id` suffix:

```
Order.Customer      → FK column "Customer"    (not "CustomerId")
Order.Payee         → FK column "Payee"       (not "PersonId")
Order.OnBehalfOf    → FK column "OnBehalfOf"  (not "PersonId")
```

Under snake_case these become `customer`, `payee`, `on_behalf_of`.

EF Core creates a shadow property named `CustomerId` internally — the library renames only the database column. Scalar FK properties on the domain object are optional — add them only if you need to set the FK without loading the navigation.

---

## Bi-directional relationships

When the principal has a collection back to the dependent, the builder wires both sides automatically:

```csharp
public class Customer : IEntity
{
    public int Id { get; set; }

    // Inverse collection — private setter required
    public ICollection<Order> Orders { get; private set; } = new List<Order>();
}

public class Order : IEntity
{
    public int      Id       { get; set; }
    public Customer Customer { get; set; } = null!;  // required FK
}
```

EF Core model:
```
Order.Customer → HasOne(Customer).WithMany(Orders).HasForeignKey("Customer").IsRequired(true)
```

---

## Uni-directional relationships

When the principal has no inverse collection, the builder configures a uni-directional relationship:

```csharp
public class Order : IEntity
{
    public int     Id      { get; set; }
    public Address Address { get; set; } = null!;  // no inverse collection on Address
}
```

EF Core model:
```
Order.Address → HasOne(Address).WithMany().HasForeignKey("Address").IsRequired(true)
```

---

## Multiple collections of the same type

When a principal entity has more than one collection of the same dependent type, the builder disambiguates using a `StartsWith` name convention — matching the collection name to the navigation property name on the dependent.

### The pattern

Name each collection on the principal so it **starts with** the navigation property name on the dependent:

```csharp
public class Withholding : IEntity
{
    public int    Id          { get; set; }

    // Three navigations to Person — each with a different semantic meaning
    public Person  Person      { get; set; } = null!;  // required
    public Person  Payee       { get; set; } = null!;  // required
    public Person? OnBehalfOf  { get; set; }            // optional
}

public class Person : IEntity
{
    public int Id { get; set; }

    // "PersonWithholdings".StartsWith("Person")         → Person nav ✓
    public ICollection<Withholding> PersonWithholdings
        { get; private set; } = new List<Withholding>();

    // "PayeeWithholdings".StartsWith("Payee")           → Payee nav ✓
    public ICollection<Withholding> PayeeWithholdings
        { get; private set; } = new List<Withholding>();

    // "OnBehalfOfWithholdings".StartsWith("OnBehalfOf") → OnBehalfOf nav ✓
    public ICollection<Withholding> OnBehalfOfWithholdings
        { get; private set; } = new List<Withholding>();
}
```

### How disambiguation works

1. The builder finds all collections on the principal (`Person`) whose element type matches the dependent (`Withholding`)
2. If there is only one match — used directly, no disambiguation needed
3. If there are multiple matches — the collection whose name starts with the navigation property name is selected

### Single collection — no disambiguation needed

When there is only one collection of a given type, the builder uses it directly regardless of name:

```csharp
public class Customer : IEntity
{
    public int Id { get; set; }

    // Only one ICollection<Order> — used automatically, any name works
    public ICollection<Order> Orders { get; private set; } = new List<Order>();
}
```

---

## Querying optional navigations in LINQ

Optional navigations (`Customer?`) can be null and must be guarded before accessing properties in EF Core LINQ queries. EF Core translates the null check to efficient SQL automatically:

```csharp
// Guard with null check — EF Core translates to SQL LEFT JOIN + IS NOT NULL
.Where(r => r.Customer != null && r.Customer.Id == customerId)

// Query for null (anonymous/unassigned records)
.Where(r => r.Customer == null)
```

**Important:** Do not use helper methods like `IfNotNull()` inside EF Core LINQ expressions — EF Core cannot translate delegate calls to SQL and will throw a runtime exception. Use the explicit null check pattern above.

`IfNotNull()` is safe and useful for in-memory evaluation on already-loaded entities (ViewModels, service business logic) — just not inside `Where`, `Select`, or other EF Core LINQ expressions that translate to SQL.

---

## Scalar FK properties — when to keep them

Scalar FK properties (`CustomerId`, `AddressId` etc.) are optional in v2.3. Keep them in these specific scenarios:

**Setting FK without loading navigation** — useful in bulk operations where you have the ID but don't want to load the full entity:

```csharp
// Without scalar FK — must load the Customer object
var customer = await UnitOfWork.FindAsync<Customer>(customerId);
order.Customer = customer;

// With scalar FK — set the ID directly, no round-trip
order.CustomerId = customerId;
```

**Filtering by FK in LINQ** — both approaches work equally in EF Core LINQ:

```csharp
// Without scalar FK — navigate through the object
.Where(o => o.Customer.Id == customerId)

// With scalar FK — direct property access
.Where(o => o.CustomerId == customerId)
```

Both generate identical SQL. The choice is stylistic — use whichever reads more naturally for your team.

---

## Relationship summary table

| Scenario | Domain declaration | EF result |
|---|---|---|
| Required — attribute | `[Required] public Customer Customer` | `IsRequired(true)` |
| Required — scalar FK | `public int CustomerId` | `IsRequired(true)` |
| Required — non-nullable nav | `public Customer Customer { get; set; } = null!` | `IsRequired(true)` |
| Optional — nullable scalar | `public int? CustomerId` | `IsRequired(false)` |
| Optional — nullable nav | `public Customer? Customer` | `IsRequired(false)` |
| Bi-directional | Collection on principal + reference on dependent | `WithMany(collectionName)` |
| Uni-directional | Reference on dependent, no collection on principal | `WithMany()` |
| Multiple same-type collections | Collection name starts with nav name | `StartsWith` disambiguation |

---

## Next steps

- [[Naming Conventions]] — how FK column names are derived from navigation property names
- [[Audit Stamping]] — `IAuditable` and `AuditInterceptor`
- [[Soft Delete]] — `ISoftDelete` and the global query filter
- [[Testing]] — testing relationships with `ForTypes` and the in-memory provider
