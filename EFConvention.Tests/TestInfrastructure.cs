// =============================================================================
// EFConvention.Tests
// Infrastructure/TestInfrastructure.cs
//
// Shared test helpers used across all three test suites:
//
//   FixedUserService     — ICurrentUserService returning a known identity
//   InMemoryStoreDb      — UnitOfWork backed by EF Core's InMemory provider
//   StoreDbFixture       — base class that seeds a fresh database per test
// =============================================================================

using EFConvention.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EFConvention.Tests.Infrastructure;

// -----------------------------------------------------------------------------
// Fixed identity — deterministic audit assertions
// -----------------------------------------------------------------------------

/// <summary>
/// ICurrentUserService implementation that always returns a fixed, known
/// username. Used so audit field assertions are deterministic.
/// </summary>
public sealed class FixedUserService : ICurrentUserService
{
    public string? UserName { get; }
    public FixedUserService(string name = "test-user") => UserName = name;
}

// -----------------------------------------------------------------------------
// In-memory StoreDb
// -----------------------------------------------------------------------------

/// <summary>
/// Concrete UnitOfWork backed by the EF Core InMemory provider.
/// Each instance uses a unique database name so tests are fully isolated.
/// The AuditInterceptor is wired exactly as it would be in production —
/// via AddInterceptors() on DbContextOptionsBuilder.
/// </summary>
public sealed class InMemoryStoreDb : UnitOfWork
{
    private readonly AuditInterceptor _audit;

    public InMemoryStoreDb(ICurrentUserService currentUser)
        : base(
            domainAssembly:       typeof(Customer).Assembly,
            configureConventions: b => b.UseSnakeCase().WithFullAudit())
    {
        _audit = new AuditInterceptor(currentUser);
    }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        if (!options.IsConfigured)
            options
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w =>
                    w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .EnableServiceProviderCaching(false)
                .AddInterceptors(_audit);
    }
}

// -----------------------------------------------------------------------------
// Base fixture — creates a fresh seeded database per test class
// -----------------------------------------------------------------------------

/// <summary>
/// Base class for integration tests. Creates an <see cref="InMemoryStoreDb"/>
/// and provides helpers for building common domain objects.
/// Override <see cref="SeedAsync"/> to insert data before each test.
/// </summary>
public abstract class StoreDbFixture : IDisposable
{
    protected readonly InMemoryStoreDb Db;
    protected readonly ICurrentUserService User;
    protected const string TestUser = "test-user";

    protected StoreDbFixture()
    {
        User = new FixedUserService(TestUser);
        Db   = new InMemoryStoreDb(User);
        Db.Database.EnsureCreated();
        SeedAsync().GetAwaiter().GetResult();
    }

    /// <summary>Override to seed data before each test runs.</summary>
    protected virtual Task SeedAsync() => Task.CompletedTask;

    public void Dispose() => Db.Dispose();

    // -------------------------------------------------------------------------
    // Domain object factory helpers
    // -------------------------------------------------------------------------

    protected static Address NewAddress(
        string street = "123 Main St",
        string city   = "Springfield",
        string state  = "IL",
        string zip    = "62701") => new()
    {
        Street     = street,
        City       = city,
        State      = state,
        PostalCode = zip
    };

    protected static Customer NewCustomer(Address address,
        string name  = "Alice Smith",
        string email = "alice@example.com") => new()
    {
        Name      = name,
        Email     = email,
        Phone     = "555-0100",
        AddressId = address.Id,
        Address   = address
    };

    protected static Category NewCategory(
        string name = "Electronics") => new()
    {
        Name        = name,
        Description = $"{name} products"
    };

    protected static Product NewProduct(Category category,
        string  name  = "Laptop",
        decimal price = 999.99m) => new()
    {
        Name        = name,
        Description = "A product",
        Sku         = $"SKU-{name.ToUpper().Replace(" ", "-")}",
        Price       = price,
        CostPrice   = price * 0.6m,
        CategoryId  = category.Id,
        Category    = category
    };

    protected static Order NewOrder(Customer customer,
        decimal total = 999.99m) => new()
    {
        OrderDate   = DateTime.UtcNow,
        Status      = "Pending",
        TotalAmount = total,
        CustomerId  = customer.Id,
        Customer    = customer
    };

    protected static ProductReview NewReview(Product product,
        int     rating     = 5,
        int?    customerId = null,
        string  comment    = "Great product!") => new()
    {
        Rating     = rating,
        Comment    = comment,
        ProductId  = product.Id,
        Product    = product,
        CustomerId = customerId
    };

    // -------------------------------------------------------------------------
    // Async seed helpers — persist objects and return them with Ids populated
    // -------------------------------------------------------------------------

    protected async Task<Address> AddAsync(Address a)
    { Db.Add(a); await Db.CompleteAsync(); return a; }

    protected async Task<Category> AddAsync(Category c)
    { Db.Add(c); await Db.CompleteAsync(); return c; }

    protected async Task<Customer> AddAsync(Customer c)
    { Db.Add(c); await Db.CompleteAsync(); return c; }

    protected async Task<Product> AddAsync(Product p)
    { Db.Add(p); await Db.CompleteAsync(); return p; }

    protected async Task<Order> AddAsync(Order o)
    { Db.Add(o); await Db.CompleteAsync(); return o; }

    protected async Task<ProductReview> AddAsync(ProductReview r)
    { Db.Add(r); await Db.CompleteAsync(); return r; }
}
