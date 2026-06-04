# Unit of Work

The unit of work pattern groups related database operations into a single transaction. EFConventionBuilder provides `IUnitOfWork` and `UnitOfWork` — a testable interface and an abstract EF Core `DbContext` base that implements it.

---

## Why unit of work

Without a unit of work, services call `SaveChanges` independently. This creates two problems:

**Partial saves** — if a service method adds an order and then updates inventory, a failure between the two saves leaves the database in an inconsistent state. With a unit of work, both changes accumulate in the change tracker and commit atomically in a single `CompleteAsync` call.

**Testability** — services that depend directly on a concrete `DbContext` are difficult to unit test. Services that depend on `IUnitOfWork` can be tested with a mock — no database required.

---

## IUnitOfWork

All application services depend on `IUnitOfWork`, never on the concrete `StoreDb`. This is what enables clean unit tests and loose coupling between your service layer and persistence layer.

```csharp
public interface IUnitOfWork : IDisposable
{
    // Transaction boundary — flush all change tracker entries to the database
    Task CompleteAsync(CancellationToken ct = default);

    // Query — compose LINQ queries against the database
    IQueryable<TEntity> Query<TEntity>() where TEntity : class;

    // Commands — modify EF's change tracker (no I/O until CompleteAsync)
    void Add<TEntity>(TEntity entity)    where TEntity : class;
    void Update<TEntity>(TEntity entity) where TEntity : class;
    void Remove<TEntity>(TEntity entity) where TEntity : class;

    // Lookup — uses EF identity map, avoids a round-trip if already tracked
    ValueTask<TEntity?> FindAsync<TEntity>(int id, CancellationToken ct = default)
        where TEntity : class;

    // Refresh — reload navigation properties from the database
    Task RefreshAsync<TEntity>(TEntity entity,
        params Expression<Func<TEntity, object>>[] references)
        where TEntity : class;

    Task RefreshCollectionAsync<TEntity, TElement>(TEntity entity,
        Expression<Func<TEntity, ICollection<TElement>>> collection,
        CancellationToken ct = default)
        where TEntity : class where TElement : class;
}
```

---

## Why Add / Update / Remove are synchronous

`Add`, `Update`, and `Remove` are synchronous because they only touch EF Core's **in-memory change tracker** — no database I/O occurs. The actual INSERT, UPDATE, or DELETE statement is deferred until `CompleteAsync` is called.

```csharp
// These three lines touch only memory — no database round-trips
UnitOfWork.Add(order);
UnitOfWork.Update(customer);
UnitOfWork.Remove(oldAddress);

// This single call sends all three changes to the database atomically
await UnitOfWork.CompleteAsync(ct);
```

This is the core benefit of the pattern — accumulate changes freely, commit once.

---

## CompleteAsync

`CompleteAsync` calls `SaveChangesAsync` on the underlying EF Core `DbContext`. All pending change tracker entries are flushed to the database in a single transaction.

```csharp
public async Task PlaceOrderAsync(Order order, CancellationToken ct = default)
{
    // Validate
    if (order.Items.Count == 0)
        throw new InvalidOperationException("Order must have at least one item.");

    // Stage changes — no database I/O yet
    UnitOfWork.Add(order);

    foreach (var item in order.Items)
        UnitOfWork.Add(item);

    // Commit all changes atomically — one transaction, one round-trip
    await UnitOfWork.CompleteAsync(ct);
}
```

If `SaveChangesAsync` throws, none of the changes are committed — the transaction rolls back automatically.

---

## Query

`Query<TEntity>()` returns an `IQueryable<TEntity>` for composing LINQ queries against the database. EF Core translates the LINQ expression tree to SQL and executes it when the query is materialised (`.ToListAsync()`, `.FirstOrDefaultAsync()` etc.).

```csharp
// Simple query
var orders = await UnitOfWork.Query<Order>()
    .Where(o => o.Status == "Pending")
    .OrderByDescending(o => o.OrderDate)
    .ToListAsync(ct);

// With includes
var order = await UnitOfWork.Query<Order>()
    .Include(o => o.Customer)
    .Include(o => o.Items)
        .ThenInclude(i => i.Product)
    .FirstOrDefaultAsync(o => o.Id == id, ct);

// Navigate through optional FK — guard against null
var reviews = await UnitOfWork.Query<ProductReview>()
    .Where(r => r.Customer != null && r.Customer.Id == customerId)
    .ToListAsync(ct);

// Bypassing soft delete filter
var deletedOrders = await UnitOfWork.Query<Order>()
    .IgnoreQueryFilters()
    .Where(o => o.IsDeleted)
    .ToListAsync(ct);
```

---

## FindAsync

`FindAsync` uses EF Core's identity map — if the entity is already tracked in the current `DbContext` scope, it returns the cached instance without hitting the database. Falls back to a database query if not tracked.

```csharp
// Returns cached instance if already tracked — avoids redundant round-trips
var customer = await UnitOfWork.FindAsync<Customer>(customerId, ct);
```

Use `FindAsync` when you have an ID and just need the entity. Use `Query<T>()` when you need to compose a more complex query with filters, includes, or ordering.

---

## RefreshAsync

Reloads navigation properties from the database after a save. Useful when you need updated collection state after `CompleteAsync`:

```csharp
// Add a new order item and reload the Items collection
UnitOfWork.Add(newItem);
await UnitOfWork.CompleteAsync(ct);

// Reload the Items collection to reflect the new item
await UnitOfWork.RefreshCollectionAsync(order, o => o.Items, ct);

// Reload reference navigations
await UnitOfWork.RefreshAsync(order, o => o.Customer);
```

---

## Working with multiple entity types

`IUnitOfWork` is not tied to a single entity type — any service can query and modify any entity type through the same unit of work. All changes accumulate in the same change tracker and commit atomically.

```csharp
public sealed class OrderService : ServiceBase<Order>, IOrderService
{
    public async Task PlaceOrderAsync(
        int customerId, List<OrderItem> items, CancellationToken ct = default)
    {
        // Read from multiple entity types
        var customer = await UnitOfWork.Query<Customer>()
            .FirstOrDefaultAsync(c => c.Id == customerId, ct)
            ?? throw new KeyNotFoundException($"Customer {customerId} not found.");

        foreach (var item in items)
        {
            var product = await UnitOfWork.Query<Product>()
                .FirstOrDefaultAsync(p => p.Id == item.Product.Id, ct)
                ?? throw new KeyNotFoundException($"Product {item.Product.Id} not found.");

            if (product.IsDeleted)
                throw new InvalidOperationException(
                    $"Product {product.Name} is no longer available.");
        }

        // Build the order
        var order = new Order
        {
            Customer    = customer,
            OrderDate   = DateTime.UtcNow,
            Status      = "Pending",
            TotalAmount = items.Sum(i => i.UnitPrice * i.Quantity)
        };

        // Stage all changes — Order, plus each OrderItem
        UnitOfWork.Add(order);
        await UnitOfWork.CompleteAsync(ct);  // get Order.Id

        foreach (var item in items)
        {
            item.Order = order;
            UnitOfWork.Add(item);
        }

        // Commit all items atomically with the order
        await UnitOfWork.CompleteAsync(ct);
    }
}
```

The `TEntity` type parameter on `ServiceBase<TEntity>` only controls the delete/restore/purge helpers — it does not restrict which entity types the service can query or modify.

---

## Subclassing UnitOfWork

Create one concrete subclass per database. This is the only class in your application that knows the connection string, domain assembly anchor, and convention configuration:

```csharp
public sealed class StoreDb : UnitOfWork
{
    private readonly string _connectionString;
    private readonly AuditInterceptor _auditInterceptor;

    // typeof(Customer) is just an assembly scan anchor —
    // any domain type in the assembly works
    public StoreDb(string connectionString, ICurrentUserService currentUser)
        : base(
            domainAssembly:       typeof(Customer).Assembly,
            configureConventions: b => b.WithFullAudit())
    {
        _connectionString = connectionString;
        _auditInterceptor  = new AuditInterceptor(currentUser);
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

**Important:** Always call `base.OnModelCreating(modelBuilder)` before `_conventions.Apply(modelBuilder)` if you override `OnModelCreating`. The `UnitOfWork` base class handles this correctly — only override if you need additional configuration after the conventions are applied.

---

## DI registration

### ASP.NET Core

```csharp
// Program.cs
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, HttpContextCurrentUserService>();

builder.Services.AddScoped<IUnitOfWork>(sp =>
    new StoreDb(
        builder.Configuration.GetConnectionString("StoreDb")!,
        sp.GetRequiredService<ICurrentUserService>()));
```

### Without audit stamping

```csharp
services.AddScoped<IUnitOfWork>(sp => new SimpleDb(connectionString));
```

### Multiple databases

Register each database under a named or keyed service, or use separate interfaces:

```csharp
// Two separate IUnitOfWork registrations — use keyed services (.NET 8+)
builder.Services.AddKeyedScoped<IUnitOfWork>("store", (sp, _) =>
    new StoreDb(
        builder.Configuration.GetConnectionString("StoreDb")!,
        sp.GetRequiredService<ICurrentUserService>()));

builder.Services.AddKeyedScoped<IUnitOfWork>("reporting", (sp, _) =>
    new ReportingDb(
        builder.Configuration.GetConnectionString("ReportingDb")!));
```

---

## ValueTask vs Task on FindAsync

`FindAsync` returns `ValueTask<TEntity?>` rather than `Task<TEntity?>`. This is a performance optimisation — when the entity is already in the EF Core identity map (already tracked in the current scope), `FindAsync` returns synchronously without allocating a `Task` object. `ValueTask` is the correct return type for operations that are frequently synchronous.

```csharp
// May complete synchronously (no allocation) if entity is already tracked
var customer = await UnitOfWork.FindAsync<Customer>(id, ct);

// Always async — always hits the database
var customer = await UnitOfWork.Query<Customer>()
    .FirstOrDefaultAsync(c => c.Id == id, ct);
```

---

## Unit testing with IUnitOfWork

Because services depend on `IUnitOfWork` rather than the concrete `DbContext`, you can mock the interface with Moq and test service logic without a database:

```csharp
public class CustomerServiceTests
{
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<ICurrentUserService> _user = new();
    private readonly CustomerService _svc;

    public CustomerServiceTests()
    {
        _user.Setup(u => u.UserName).Returns("test-user");
        _svc = new CustomerService(_uow.Object, _user.Object);
    }

    [Fact]
    public async Task DeleteCustomerAsync_CallsRemove_WhenCustomerExists()
    {
        var customer = new Customer { Id = 1, Name = "Alice",
                                      Address = new Address() };
        _uow.Setup(u => u.Query<Customer>())
            .Returns(new[] { customer }.AsQueryable());

        await _svc.DeleteCustomerAsync(1);

        _uow.Verify(u => u.Remove(customer), Times.Once);
        _uow.Verify(u => u.CompleteAsync(default), Times.Once);
    }
}
```

See [[Testing]] for a complete guide to unit and integration testing.

---

## Data access patterns — Find vs FindAsync vs Query

Three methods are available for reading data from the database. Choosing the right one communicates intent clearly:

| Method | Intent | When to use |
|---|---|---|
| `FindAsync<T>(id)` | Lookup by primary key — simple | Know the ID, no includes needed. Uses EF identity map — returns cached instance if already tracked. |
| `Query<T>()` | Search by criteria — compose freely | Filtering by properties, ordering, projections, or any query that isn't a simple PK lookup. |

```csharp
// FindAsync — simple PK lookup, identity map benefit
var customer = await UnitOfWork.FindAsync<Customer>(id, ct);

// Query — search by criteria
var pendingOrders = await UnitOfWork.Query<Order>()
    .Where(o => o.Status == "Pending")
    .Include(o => o.Customer)
    .OrderByDescending(o => o.OrderDate)
    .ToListAsync(ct);

// Query — PK lookup with includes (when FindAsync isn't enough)
var order = await UnitOfWork.Query<Order>()
    .Include(o => o.Customer)
    .Include(o => o.Items)
    .FirstOrDefaultAsync(o => o.Id == id, ct);
```

**Coming in v2.4** — a `Find<T>(id)` method returning `IQueryable<T>` pre-filtered by PK for fluent composition:

```csharp
// Find — PK anchor with free composition (v2.4)
var order = await UnitOfWork.Find<Order>(id)
    .Include(o => o.Customer)
    .Include(o => o.Items)
    .FirstOrDefaultAsync(ct);

// Find with soft delete bypass (v2.4)
var order = await UnitOfWork.Find<Order>(id)
    .IgnoreQueryFilters()
    .FirstOrDefaultAsync(ct);
```

The three-method vocabulary will be:
- `FindAsync` — simple PK lookup, identity map, no composition
- `Find` — PK anchor, composable (v2.4)
- `Query` — criteria-based search, full LINQ composition

---

## Next steps

- [[Service Base]] — `DeleteAsync`, `RestoreAsync`, and `PurgeAsync` built on top of `IUnitOfWork`
- [[Testing]] — mocking `IUnitOfWork` with Moq, integration testing with in-memory provider
- [[Audit Stamping]] — how `AuditInterceptor` hooks into the save pipeline
