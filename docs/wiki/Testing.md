# Testing

EFConventionBuilder is designed for testability. Services depend on `IUnitOfWork` — a mockable interface — and the library provides `ForTypes()` for isolated model testing without assembly scanning side effects.

---

## Two testing approaches

| Approach | When to use | Tools |
|---|---|---|
| **Unit tests** | Test service business logic — validation, branching, error handling | Moq, xUnit |
| **Integration tests** | Test EF Core model configuration, queries, soft delete, audit stamping | EF Core InMemory provider, xUnit |

---

## Unit testing services with Moq

Unit tests mock `IUnitOfWork` and `ICurrentUserService` — no database, no EF Core, pure logic testing.

### Test infrastructure

```csharp
// Fixed identity for deterministic audit assertions
public class FixedUserService : ICurrentUserService
{
    public string? UserName { get; }
    public FixedUserService(string name = "test-user") => UserName = name;
}
```

### Basic service test

```csharp
public class OrderServiceTests
{
    private readonly Mock<IUnitOfWork>         _uow  = new();
    private readonly Mock<ICurrentUserService> _user = new();
    private readonly OrderService              _svc;

    public OrderServiceTests()
    {
        _user.Setup(u => u.UserName).Returns("test-user");
        _svc = new OrderService(_uow.Object, _user.Object);
    }

    [Fact]
    public async Task DeleteOrderAsync_SoftDeletes_WhenOrderExists()
    {
        // Arrange
        var order = new Order
        {
            Id        = 1,
            OrderDate = DateTime.UtcNow,
            Customer  = new Customer { Address = new Address() },
            IsDeleted = false
        };

        _uow.Setup(u => u.Query<Order>())
            .Returns(new[] { order }.AsQueryable());

        // Act
        await _svc.DeleteOrderAsync(1);

        // Assert — Order implements ISoftDelete → soft delete, not Remove
        _uow.Verify(u => u.Remove(It.IsAny<Order>()), Times.Never);
        _uow.Verify(u => u.CompleteAsync(default), Times.Once);
        order.IsDeleted.Should().BeTrue();
        order.DeletedBy.Should().Be("test-user");
        order.DeletedDate.Should().NotBeNull();
    }

    [Fact]
    public async Task DeleteOrderAsync_Throws_WhenOrderNotFound()
    {
        _uow.Setup(u => u.Query<Order>())
            .Returns(Enumerable.Empty<Order>().AsQueryable());

        var act = async () => await _svc.DeleteOrderAsync(99);

        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("*99*");
    }

    [Fact]
    public async Task RestoreOrderAsync_ClearsSoftDeleteFields()
    {
        var order = new Order
        {
            Id          = 1,
            OrderDate   = DateTime.UtcNow,
            Customer    = new Customer { Address = new Address() },
            IsDeleted   = true,
            DeletedDate = DateTime.UtcNow.AddDays(-1),
            DeletedBy   = "admin"
        };

        _uow.Setup(u => u.Query<Order>())
            .Returns(new[] { order }.AsQueryable());

        await _svc.RestoreOrderAsync(1);

        order.IsDeleted.Should().BeFalse();
        order.DeletedDate.Should().BeNull();
        order.DeletedBy.Should().BeNull();
        _uow.Verify(u => u.CompleteAsync(default), Times.Once);
    }
}
```

### Testing hard delete (entity without ISoftDelete)

```csharp
public class CustomerServiceTests
{
    private readonly Mock<IUnitOfWork>         _uow  = new();
    private readonly Mock<ICurrentUserService> _user = new();
    private readonly CustomerService           _svc;

    public CustomerServiceTests()
    {
        _user.Setup(u => u.UserName).Returns("test-user");
        _svc = new CustomerService(_uow.Object, _user.Object);
    }

    [Fact]
    public async Task DeleteCustomerAsync_HardDeletes_WhenCustomerExists()
    {
        // Arrange
        var customer = new Customer
        {
            Id      = 1,
            Name    = "Alice",
            Email   = "alice@example.com",
            Address = new Address()
        };

        _uow.Setup(u => u.Query<Customer>())
            .Returns(new[] { customer }.AsQueryable());

        // Act
        await _svc.DeleteCustomerAsync(1);

        // Assert — Customer does not implement ISoftDelete → physical Remove
        _uow.Verify(u => u.Remove(customer), Times.Once);
        _uow.Verify(u => u.CompleteAsync(default), Times.Once);
    }
}
```

---

## Integration testing with the in-memory provider

Integration tests use EF Core's in-memory provider to test the full stack — model configuration, queries, relationships, soft delete filters, and audit stamping — without a real database.

### Test infrastructure

```csharp
// Fixed identity service for deterministic audit assertions
public class FixedUserService : ICurrentUserService
{
    public string? UserName { get; }
    public FixedUserService(string name = "test-user") => UserName = name;
}

// In-memory DbContext — same configuration as production
public sealed class InMemoryStoreDb : UnitOfWork
{
    private readonly AuditInterceptor _auditInterceptor;

    public InMemoryStoreDb(ICurrentUserService? currentUser = null)
        : base(
            domainAssembly:       typeof(Customer).Assembly,
            configureConventions: b => b.WithFullAudit())
    {
        var user = currentUser ?? new FixedUserService();
        _auditInterceptor = new AuditInterceptor(user);
    }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        if (!options.IsConfigured)
            options
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .EnableServiceProviderCaching(false)
                .ConfigureWarnings(w =>
                    w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .AddInterceptors(_auditInterceptor);
    }
}
```

**Important notes:**
- `Guid.NewGuid().ToString()` gives each test a fresh database — tests are fully isolated
- `EnableServiceProviderCaching(false)` prevents EF Core from caching the model between tests — required when tests use different configurations
- `ConfigureWarnings` suppresses the in-memory transaction warning — in-memory doesn't support real transactions but the warning is noise in tests

### Test fixture

```csharp
// Shared fixture — creates the database once per test class
public class StoreDbFixture : IDisposable
{
    public InMemoryStoreDb Db    { get; }
    public FixedUserService User { get; }

    public StoreDbFixture()
    {
        User = new FixedUserService("test-user");
        Db   = new InMemoryStoreDb(User);
    }

    public void Dispose() => Db.Dispose();
}
```

### Integration test example

```csharp
public class StoreIntegrationTests : IClassFixture<StoreDbFixture>
{
    private readonly InMemoryStoreDb  _db;
    private readonly FixedUserService _user;
    private readonly OrderService     _svc;

    // Seed data
    private readonly Address  _address  = new() { Street = "123 Main St", City = "Springfield", State = "IL", PostalCode = "62701" };
    private readonly Category _category = new() { Name = "Electronics" };
    private Customer _customer = null!;
    private Product  _product  = null!;

    public StoreIntegrationTests(StoreDbFixture fixture)
    {
        _db   = fixture.Db;
        _user = fixture.User;
        _svc  = new OrderService(_db, _user);
    }

    // Helper factory methods
    protected static Order NewOrder(Customer customer) => new()
    {
        Customer  = customer,
        OrderDate = DateTime.UtcNow,
        Status    = "Pending"
    };

    [Fact]
    public async Task AddOrder_SetsAuditFields()
    {
        var order = NewOrder(_customer);
        _db.Add(order);
        await _db.CompleteAsync();

        order.CreatedBy.Should().Be("test-user");
        order.CreatedDate.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        order.ModifiedDate.Should().BeNull();
        order.ModifiedBy.Should().BeNull();
    }

    [Fact]
    public async Task DeleteOrder_SoftDeletes_HiddenFromNormalQuery()
    {
        var order = NewOrder(_customer);
        _db.Add(order);
        await _db.CompleteAsync();

        await _svc.DeleteOrderAsync(order.Id);

        // Normal query — soft-deleted rows hidden by global filter
        var active = await _db.Query<Order>().ToListAsync();
        active.Should().NotContain(o => o.Id == order.Id);

        // IgnoreQueryFilters — soft-deleted rows visible
        var deleted = await _db.Query<Order>()
            .IgnoreQueryFilters()
            .Where(o => o.IsDeleted)
            .ToListAsync();
        deleted.Should().Contain(o => o.Id == order.Id);
    }

    [Fact]
    public async Task RestoreOrder_MakesOrderVisibleAgain()
    {
        var order = NewOrder(_customer);
        _db.Add(order);
        await _db.CompleteAsync();

        await _svc.DeleteOrderAsync(order.Id);
        await _svc.RestoreOrderAsync(order.Id);

        var active = await _db.Query<Order>().ToListAsync();
        active.Should().Contain(o => o.Id == order.Id);

        order.IsDeleted.Should().BeFalse();
        order.DeletedDate.Should().BeNull();
        order.DeletedBy.Should().BeNull();
    }

    [Fact]
    public async Task PurgeOrder_RequiresPriorSoftDelete()
    {
        var order = NewOrder(_customer);
        _db.Add(order);
        await _db.CompleteAsync();

        // Purge without soft deleting first → should throw
        var act = async () => await _svc.PurgeOrderAsync(order.Id);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
```

---

## Testing model configuration with ForTypes

`ForTypes` lets you test specific relationship configurations in isolation — without scanning the entire test assembly and picking up unrelated test fixtures (like `BadEntity` used in startup validation tests).

```csharp
// Test entities — private to the test class, no assembly scanning side effects
private sealed class Principal : IEntity
{
    public int Id { get; set; }

    public ICollection<Dependent> PrimaryDependents
        { get; private set; } = new List<Dependent>();
    public ICollection<Dependent> SecondaryDependents
        { get; private set; } = new List<Dependent>();
}

private sealed class Dependent : IEntity
{
    public int Id { get; set; }

    public Principal  Primary   { get; set; } = null!;  // required
    public Principal? Secondary { get; set; }            // optional
}

private sealed class TestDb : DbContext
{
    public TestDb() : base(
        new DbContextOptionsBuilder<TestDb>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .EnableServiceProviderCaching(false)
            .Options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        EntityConventionBuilder
            .ForTypes(typeof(Principal), typeof(Dependent))  // ← isolated scan
            .Apply(modelBuilder);
    }
}

[Fact]
public void NonNullableNavigation_IsRequired()
{
    using var db = new TestDb();
    var fk = db.Model.FindEntityType(typeof(Dependent))!
        .GetForeignKeys()
        .First(f => f.DependentToPrincipal?.Name == "Primary");

    fk.IsRequired.Should().BeTrue();
}

[Fact]
public void NullableNavigation_IsOptional()
{
    using var db = new TestDb();
    var fk = db.Model.FindEntityType(typeof(Dependent))!
        .GetForeignKeys()
        .First(f => f.DependentToPrincipal?.Name == "Secondary");

    fk.IsRequired.Should().BeFalse();
}
```

---

## Testing naming conventions

Use a `TestDb` helper that accepts a builder configuration lambda — lets you test the same domain under different conventions:

```csharp
public class NamingConventionTests
{
    private sealed class TestDb : DbContext
    {
        private readonly Action<EntityConventionBuilder> _configure;

        public TestDb(Action<EntityConventionBuilder> configure) : base(
            new DbContextOptionsBuilder<TestDb>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .EnableServiceProviderCaching(false)
                .Options)
        {
            _configure = configure;
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            var builder = EntityConventionBuilder.ForAssemblyOf<Customer>();
            _configure(builder);
            builder.Apply(modelBuilder);
        }
    }

    [Fact]
    public void SnakeCase_TableName_IsLowercase()
    {
        using var db = new TestDb(b => b.UseSnakeCase());
        db.Model.FindEntityType(typeof(Customer))!
            .GetTableName()
            .Should().Be("customer");
    }

    [Fact]
    public void PascalCase_TableName_MatchesTypeName()
    {
        using var db = new TestDb(b => { });  // default — PascalCase
        db.Model.FindEntityType(typeof(Customer))!
            .GetTableName()
            .Should().Be("Customer");
    }

    [Fact]
    public void SnakeCase_FkColumn_IsNavigationName_Lowercased()
    {
        using var db = new TestDb(b => b.UseSnakeCase());
        db.Model.FindEntityType(typeof(Order))!
            .GetForeignKeys()
            .First(f => f.PrincipalEntityType.ClrType == typeof(Customer))
            .Properties.Single()
            .GetColumnName()
            .Should().Be("customer");
    }
}
```

---

## Tips

**Use `Guid.NewGuid()` as the database name** — guarantees test isolation. Two tests can run in parallel without sharing state.

**Use `EnableServiceProviderCaching(false)`** — required when tests configure the model differently. Without it EF Core caches the first model it builds and reuses it for all tests, causing unexpected failures.

**Use `IgnoreQueryFilters()` in delete/restore tests** — soft-deleted rows are hidden by default. You must explicitly bypass the global filter to assert on them.

**Keep test entity types private** — `ForTypes` exists precisely to avoid assembly scanning picking up test-only types. Private nested classes are invisible to the assembly scanner but fully usable with `ForTypes`.

**Use `FixedUserService` for audit assertions** — `SystemUserService` returns `"system"` which is fine for most tests. Use `FixedUserService` when you need to assert on specific `CreatedBy` or `ModifiedBy` values.

---

## Next steps

- [[Unit of Work]] — `IUnitOfWork` interface details
- [[Service Base]] — `DeleteAsync`, `RestoreAsync`, and `PurgeAsync`
- [[Soft Delete]] — global query filter and `IgnoreQueryFilters`
