# Soft Delete

EFConventionBuilder provides automatic soft delete via a global EF Core query filter. Entities implementing `ISoftDelete` are never physically removed from the database — they are hidden from all normal queries by a `WHERE IsDeleted = 0` filter applied automatically.

---

## How it works

When `WithSoftDelete()` or `WithFullAudit()` is enabled and an entity implements `ISoftDelete`:

1. A global EF Core query filter is registered: `WHERE IsDeleted = 0`
2. Every normal query against that entity automatically excludes deleted rows
3. `ServiceBase.DeleteAsync` sets `IsDeleted = true` and stamps `DeletedDate` / `DeletedBy`
4. The physical row is **never removed** — it stays in the database, invisible to normal queries
5. Deleted rows can be reached by calling `.IgnoreQueryFilters()` explicitly

---

## The ISoftDelete interface

Implement `ISoftDelete` on any entity that should be soft-deleted:

```csharp
public interface ISoftDelete
{
    bool      IsDeleted   { get; set; }
    DateTime? DeletedDate { get; set; }
    string?   DeletedBy   { get; set; }
}
```

`IsDeleted` — `false` for active rows, `true` for soft-deleted rows.
`DeletedDate` — `null` for active rows, UTC timestamp when soft-deleted.
`DeletedBy` — `null` for active rows, identity of the user who deleted the record.

### Domain entity example

```csharp
public class Order : IEntity, IAuditable, ISoftDelete
{
    public int      Id          { get; set; }
    public DateTime OrderDate   { get; set; }
    public string   Status      { get; set; } = "Pending";

    [Precision(18, 2)]
    public decimal TotalAmount { get; set; }

    public Customer Customer { get; set; } = null!;

    public ICollection<OrderItem> Items
        { get; private set; } = new List<OrderItem>();

    // IAuditable — stamped by AuditInterceptor
    public DateTime  CreatedDate  { get; set; }
    public string    CreatedBy    { get; set; } = string.Empty;
    public DateTime? ModifiedDate { get; set; }
    public string?   ModifiedBy   { get; set; }

    // ISoftDelete — managed by ServiceBase.DeleteAsync
    // Never set these manually — use DeleteAsync/RestoreAsync/PurgeAsync
    public bool      IsDeleted   { get; set; }
    public DateTime? DeletedDate { get; set; }
    public string?   DeletedBy   { get; set; }
}
```

Never set `IsDeleted`, `DeletedDate`, or `DeletedBy` manually in your services. Use `ServiceBase.DeleteAsync`, `RestoreAsync`, and `PurgeAsync` — they handle stamping consistently.

---

## Enabling soft delete

Soft delete requires two things — both must be present:

1. The entity implements `ISoftDelete`
2. The builder is configured with `.WithSoftDelete()` or `.WithFullAudit()`

```csharp
// Soft delete only
configureConventions: b => b.WithSoftDelete()

// Soft delete + audit together
configureConventions: b => b.WithFullAudit()
```

### Per-entity opt-in

| Entity implements | Builder configured with | Global filter applied? |
|---|---|---|
| `ISoftDelete` | `.WithSoftDelete()` or `.WithFullAudit()` | ✅ Yes |
| `ISoftDelete` | no soft delete flag | ❌ No |
| neither | anything | ❌ No |

---

## The delete / restore / purge lifecycle

```
AddAsync        → IsDeleted = false  (active, visible to all queries)
        ↓
DeleteAsync     → IsDeleted = true   (hidden by global query filter)
        ↓
    (choice)
       ↙                ↘
RestoreAsync          PurgeAsync
IsDeleted = false     Physical DELETE
(visible again)       (permanent, row gone)
```

### `DeleteAsync` — soft delete

Called via `ServiceBase<TEntity>.DeleteAsync`. Sets `IsDeleted = true`, stamps `DeletedDate` and `DeletedBy`, saves:

```csharp
public async Task DeleteOrderAsync(int id, CancellationToken ct = default)
{
    var order = await UnitOfWork.Query<Order>()
        .FirstOrDefaultAsync(o => o.Id == id, ct)
        ?? throw new KeyNotFoundException($"Order {id} not found.");

    // Order implements ISoftDelete → soft delete chosen automatically
    // Remove ISoftDelete from Order → this becomes a hard delete
    await DeleteAsync(order, ct);
}
```

`DeleteAsync` chooses the delete path **at runtime**:
- Entity implements `ISoftDelete` → soft delete
- Entity does not implement `ISoftDelete` → physical `DELETE`

This means removing `ISoftDelete` from an entity type changes the delete behaviour without touching any service code.

### `RestoreAsync` — reverse a soft delete

Clears all soft delete fields — the row becomes visible to normal queries again:

```csharp
public async Task RestoreOrderAsync(int id, CancellationToken ct = default)
{
    // Must bypass the global filter to find soft-deleted rows
    var order = await UnitOfWork.Query<Order>()
        .IgnoreQueryFilters()
        .FirstOrDefaultAsync(o => o.Id == id && o.IsDeleted, ct)
        ?? throw new KeyNotFoundException($"Soft-deleted order {id} not found.");

    await RestoreAsync(order, ct);
}
```

Throws `InvalidOperationException` if the entity does not implement `ISoftDelete`.

### `PurgeAsync` — permanent physical delete

Permanently removes a soft-deleted row from the database. Enforces a two-step pattern — the entity must already be soft-deleted before it can be purged:

```csharp
public async Task PurgeOrderAsync(int id, CancellationToken ct = default)
{
    var order = await UnitOfWork.Query<Order>()
        .IgnoreQueryFilters()
        .FirstOrDefaultAsync(o => o.Id == id, ct)
        ?? throw new KeyNotFoundException($"Order {id} not found.");

    // PurgeAsync throws if order is still active (IsDeleted = false)
    await PurgeAsync(order, ct);
}
```

Throws `InvalidOperationException` if:
- The entity does not implement `ISoftDelete`
- The entity is still active (`IsDeleted = false`) — soft delete first, then purge

The two-step pattern is intentional — it prevents accidental permanent deletion.

---

## Querying soft-deleted rows

The global query filter is invisible — it just works. To reach soft-deleted rows call `.IgnoreQueryFilters()`:

```csharp
// Active rows only (normal query — filter applied automatically)
var activeOrders = await UnitOfWork.Query<Order>()
    .Where(o => o.Customer.Id == customerId)
    .ToListAsync();

// Soft-deleted rows only
var deletedOrders = await UnitOfWork.Query<Order>()
    .IgnoreQueryFilters()
    .Where(o => o.IsDeleted && o.Customer.Id == customerId)
    .ToListAsync();

// All rows — active and deleted
var allOrders = await UnitOfWork.Query<Order>()
    .IgnoreQueryFilters()
    .Where(o => o.Customer.Id == customerId)
    .ToListAsync();
```

---

## Hard delete — entities without ISoftDelete

When an entity does not implement `ISoftDelete`, `ServiceBase.DeleteAsync` issues a physical `DELETE`. No global query filter is registered for that entity type — all rows are always visible.

```csharp
// Customer does not implement ISoftDelete
public class Customer : IEntity, IAuditable
{
    // No IsDeleted, DeletedDate, DeletedBy
}

// CustomerService.DeleteAsync → physical DELETE, row gone permanently
await DeleteAsync(customer, ct);
```

This is the correct pattern for entities that should be permanently removed — customer account deletion, admin data cleanup, etc.

---

## Mixed domain — some soft, some hard

Different entities in the same domain can independently implement or omit `ISoftDelete`. Each entity's delete behaviour is determined solely by whether it implements the interface:

```csharp
// Soft delete — order history preserved
public class Order : IEntity, IAuditable, ISoftDelete { ... }

// Hard delete — address just gets removed
public class Address : IEntity { ... }

// Soft delete — product catalogue preserved for historical orders
public class Product : IEntity, IAuditable, ISoftDelete { ... }

// Hard delete — category removal is permanent
public class Category : IEntity { ... }
```

`ServiceBase.DeleteAsync` handles each correctly at runtime with no additional configuration.

---

## Overriding default column names

Default soft delete column names match the C# property names exactly under PascalCase:

```
IsDeleted, DeletedDate, DeletedBy
```

Override them for legacy schemas. The active naming convention is applied on top:

```csharp
.WithSoftDelete(cols =>
{
    cols.IsDeleted   = "Archived";     // → "archived" under snake_case
    cols.DeletedDate = "ArchivedDate"; // → "archived_date" under snake_case
    cols.DeletedBy   = "ArchivedBy";   // → "archived_by" under snake_case
})
```

---

## Partial index recommendation

Because every normal query filters on `IsDeleted`, a partial index on active rows significantly improves read performance — deleted rows are excluded from the index entirely:

```sql
-- PascalCase convention
CREATE INDEX IX_Order_Active
    ON dbo.[Order] (Customer, OrderDate)
    WHERE IsDeleted = 0;

-- snake_case convention
CREATE INDEX IX_order_active
    ON dbo.[order] (customer, order_date)
    WHERE is_deleted = 0;
```

---

## Next steps

- [[Service Base]] — `DeleteAsync`, `RestoreAsync`, and `PurgeAsync` in detail
- [[Audit Stamping]] — stamping `DeletedBy` and how it relates to `IAuditable`
- [[Testing]] — testing soft delete with the in-memory provider
