# Service Base

`ServiceBase<TEntity>` is an abstract base class for application services. It provides three protected helpers — `DeleteAsync`, `RestoreAsync`, and `PurgeAsync` — that implement the soft delete lifecycle correctly and consistently across your entire application.

---

## Why ServiceBase

Without `ServiceBase`, every service that deletes something must check whether the entity implements `ISoftDelete` and branch accordingly. That logic is identical across every service — it belongs in one place.

With `ServiceBase`, the delete path is determined **at runtime** from the entity type. Services just call `DeleteAsync` and the right thing happens automatically:

```csharp
// Order implements ISoftDelete → soft delete (row hidden, not removed)
await DeleteAsync(order, ct);

// Customer does not implement ISoftDelete → physical DELETE
await DeleteAsync(customer, ct);
```

If you later add or remove `ISoftDelete` from an entity, the delete behaviour changes automatically — no service code changes required.

---

## The base class

```csharp
public abstract class ServiceBase<TEntity> where TEntity : class
{
    protected IUnitOfWork        UnitOfWork  { get; }
    protected ICurrentUserService CurrentUser { get; }

    protected ServiceBase(IUnitOfWork uow, ICurrentUserService currentUser)
    {
        UnitOfWork  = uow;
        CurrentUser = currentUser;
    }

    protected async Task DeleteAsync (TEntity entity, CancellationToken ct = default);
    protected async Task RestoreAsync(TEntity entity, CancellationToken ct = default);
    protected async Task PurgeAsync  (TEntity entity, CancellationToken ct = default);
}
```

`TEntity` is the **primary entity type** for the service — it determines what `DeleteAsync`, `RestoreAsync`, and `PurgeAsync` operate on. It does not restrict which entity types the service can query or modify through `UnitOfWork`.

---

## DeleteAsync

Runtime soft/hard delete detection. Chooses the correct path based on whether `TEntity` implements `ISoftDelete`:

**Soft delete path** (entity implements `ISoftDelete`):
- Sets `IsDeleted = true`
- Stamps `DeletedDate = UtcNow`
- Stamps `DeletedBy = CurrentUser.UserName ?? "system"`
- Calls `UnitOfWork.CompleteAsync`
- Row is retained in the database, hidden by the global query filter

**Hard delete path** (entity does not implement `ISoftDelete`):
- Calls `UnitOfWork.Remove(entity)`
- Calls `UnitOfWork.CompleteAsync`
- Row is permanently removed

```csharp
public async Task DeleteOrderAsync(int id, CancellationToken ct = default)
{
    var order = await UnitOfWork.Query<Order>()
        .FirstOrDefaultAsync(o => o.Id == id, ct)
        ?? throw new KeyNotFoundException($"Order {id} not found.");

    // Order implements ISoftDelete → soft delete
    // Remove ISoftDelete from Order → becomes a hard delete
    // No code change needed here either way
    await DeleteAsync(order, ct);
}
```

---

## RestoreAsync

Reverses a soft delete. Clears all `ISoftDelete` fields and saves:

- Sets `IsDeleted = false`
- Sets `DeletedDate = null`
- Sets `DeletedBy = null`
- Calls `UnitOfWork.CompleteAsync`

The row becomes visible to normal queries again.

```csharp
public async Task RestoreOrderAsync(int id, CancellationToken ct = default)
{
    // Must use IgnoreQueryFilters — soft-deleted rows are hidden by default
    var order = await UnitOfWork.Query<Order>()
        .IgnoreQueryFilters()
        .FirstOrDefaultAsync(o => o.Id == id && o.IsDeleted, ct)
        ?? throw new KeyNotFoundException($"Soft-deleted order {id} not found.");

    await RestoreAsync(order, ct);
}
```

Throws `InvalidOperationException` if `TEntity` does not implement `ISoftDelete`.

---

## PurgeAsync

Permanently removes a soft-deleted row. Enforces a two-step pattern:

1. The entity must already be soft-deleted (`IsDeleted = true`)
2. Then call `PurgeAsync` to permanently remove it

```csharp
public async Task PurgeOrderAsync(int id, CancellationToken ct = default)
{
    var order = await UnitOfWork.Query<Order>()
        .IgnoreQueryFilters()
        .FirstOrDefaultAsync(o => o.Id == id, ct)
        ?? throw new KeyNotFoundException($"Order {id} not found.");

    // Throws if order is still active (IsDeleted = false)
    // Soft delete first, then purge
    await PurgeAsync(order, ct);
}
```

Throws `InvalidOperationException` if:
- `TEntity` does not implement `ISoftDelete`
- The entity is still active (`IsDeleted = false`)

The two-step pattern is intentional — it prevents accidentally purging an active record.

---

## Implementing a service

```csharp
public interface IOrderService
{
    Task<Order?>              GetOrderAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<Order>> GetOrdersByCustomerAsync(int customerId, CancellationToken ct = default);
    Task<IReadOnlyList<Order>> GetDeletedOrdersAsync(int customerId, CancellationToken ct = default);
    Task<Order>               AddOrderAsync(Order order, CancellationToken ct = default);
    Task                      DeleteOrderAsync(int id, CancellationToken ct = default);
    Task                      RestoreOrderAsync(int id, CancellationToken ct = default);
    Task                      PurgeOrderAsync(int id, CancellationToken ct = default);
}

public sealed class OrderService : ServiceBase<Order>, IOrderService
{
    public OrderService(IUnitOfWork uow, ICurrentUserService user)
        : base(uow, user) { }

    public async Task<Order?> GetOrderAsync(int id, CancellationToken ct = default) =>
        await UnitOfWork.Query<Order>()
            .Include(o => o.Customer)
            .Include(o => o.Items)
                .ThenInclude(i => i.Product)
            .FirstOrDefaultAsync(o => o.Id == id, ct);

    public async Task<IReadOnlyList<Order>> GetOrdersByCustomerAsync(
        int customerId, CancellationToken ct = default) =>
        await UnitOfWork.Query<Order>()
            .Where(o => o.Customer.Id == customerId)
            .OrderByDescending(o => o.OrderDate)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Order>> GetDeletedOrdersAsync(
        int customerId, CancellationToken ct = default) =>
        await UnitOfWork.Query<Order>()
            .IgnoreQueryFilters()
            .Where(o => o.IsDeleted && o.Customer.Id == customerId)
            .OrderByDescending(o => o.DeletedDate)
            .ToListAsync(ct);

    public async Task<Order> AddOrderAsync(Order order, CancellationToken ct = default)
    {
        UnitOfWork.Add(order);
        await UnitOfWork.CompleteAsync(ct);
        return order;
    }

    public async Task DeleteOrderAsync(int id, CancellationToken ct = default)
    {
        var order = await UnitOfWork.Query<Order>()
            .FirstOrDefaultAsync(o => o.Id == id, ct)
            ?? throw new KeyNotFoundException($"Order {id} not found.");
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

---

## Working with multiple entity types

`TEntity` only controls the delete/restore/purge helpers. A service can freely query and modify any entity type through `UnitOfWork`. All changes commit atomically in a single `CompleteAsync` call.

```csharp
public sealed class OrderService : ServiceBase<Order>, IOrderService
{
    // Place an order — touches Order, OrderItem, Customer, and Product
    public async Task<Order> PlaceOrderAsync(
        int customerId, List<OrderItem> items, CancellationToken ct = default)
    {
        // Read Customer
        var customer = await UnitOfWork.Query<Customer>()
            .FirstOrDefaultAsync(c => c.Id == customerId, ct)
            ?? throw new KeyNotFoundException($"Customer {customerId} not found.");

        // Validate Products
        foreach (var item in items)
        {
            var product = await UnitOfWork.Query<Product>()
                .FirstOrDefaultAsync(p => p.Id == item.Product.Id, ct)
                ?? throw new KeyNotFoundException(
                    $"Product {item.Product.Id} not found.");

            if (product.IsDeleted)
                throw new InvalidOperationException(
                    $"Product {product.Name} is no longer available.");
        }

        // Create Order
        var order = new Order
        {
            Customer    = customer,
            OrderDate   = DateTime.UtcNow,
            Status      = "Pending",
            TotalAmount = items.Sum(i => i.UnitPrice * i.Quantity)
        };

        UnitOfWork.Add(order);
        await UnitOfWork.CompleteAsync(ct);  // commit to get Order.Id

        // Add OrderItems
        foreach (var item in items)
        {
            item.Order = order;
            UnitOfWork.Add(item);
        }

        await UnitOfWork.CompleteAsync(ct);  // commit all items atomically
        return order;
    }

    // Update customer address from within OrderService
    public async Task UpdateCustomerAddressAsync(
        int customerId, Address newAddress, CancellationToken ct = default)
    {
        var customer = await UnitOfWork.Query<Customer>()
            .FirstOrDefaultAsync(c => c.Id == customerId, ct)
            ?? throw new KeyNotFoundException($"Customer {customerId} not found.");

        customer.Address = newAddress;
        UnitOfWork.Update(customer);
        await UnitOfWork.CompleteAsync(ct);
    }
}
```

---

## Services without audit stamping

If your application does not use audit stamping, `ICurrentUserService` is not needed. In this case `ServiceBase` still requires it in the constructor — pass a `SystemUserService` or create a minimal no-op implementation:

```csharp
// Option 1 — use SystemUserService (always returns "system")
services.AddScoped<ICurrentUserService, SystemUserService>();

// Option 2 — no-op implementation
public class NoOpUserService : ICurrentUserService
{
    public string? UserName => null;
}
```

---

## DI registration

```csharp
// Program.cs
builder.Services.AddScoped<IOrderService,         OrderService>();
builder.Services.AddScoped<ICustomerService,      CustomerService>();
builder.Services.AddScoped<IProductService,       ProductService>();
builder.Services.AddScoped<IProductReviewService, ProductReviewService>();
```

Services are registered against their interface — consuming classes depend on the interface, never the concrete service.

---

## Next steps

- [[Soft Delete]] — the `ISoftDelete` interface and global query filter
- [[Unit of Work]] — `IUnitOfWork` and `CompleteAsync` in detail
- [[Testing]] — unit testing services with Moq, integration testing with in-memory provider
