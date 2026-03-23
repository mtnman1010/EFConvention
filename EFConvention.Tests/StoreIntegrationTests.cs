// =============================================================================
// EFConvention.Tests
// Integration/StoreIntegrationTests.cs
//
// Full-stack integration tests using InMemoryStoreDb. These tests exercise
// the complete pipeline: domain → service → UnitOfWork → EF → in-memory DB.
//
// Suites:
//   CustomerIntegrationTests       — add, save, audit stamping, hard delete
//   OrderIntegrationTests          — soft delete lifecycle (delete/restore/purge)
//   ProductIntegrationTests        — soft delete + decimal precision
//   ProductReviewIntegrationTests  — optional FK (nullable CustomerId)
//   AuditInterceptorTests          — interceptor stamps correct fields
// =============================================================================

using EFConventions;
using EFConventions.Domain;
using EFConvention.Tests.Infrastructure;
using EFConventions.Sample.Services;
using EFConventions.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EFConvention.Tests.Integration;

// ---------------------------------------------------------------------------
// Customer — IAuditable, hard delete
// ---------------------------------------------------------------------------

public class CustomerIntegrationTests : StoreDbFixture
{
    private readonly CustomerService _svc;
    public CustomerIntegrationTests() => _svc = new CustomerService(Db, User);

    [Fact]
    public async Task AddCustomer_PersistsAndStampsAuditFields()
    {
        var address  = await AddAsync(NewAddress());
        var customer = await _svc.AddCustomerAsync(NewCustomer(address));

        var loaded = await Db.Query<Customer>()
            .FirstOrDefaultAsync(c => c.Id == customer.Id);

        loaded.Should().NotBeNull();
        loaded!.Name.Should().Be("Alice Smith");
        loaded.CreatedBy.Should().Be(TestUser,
            "AuditInterceptor stamps CreatedBy on INSERT");
        loaded.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        loaded.ModifiedAt.Should().BeNull("not yet modified");
    }

    [Fact]
    public async Task SaveCustomer_UpdatesModifiedAuditFields()
    {
        var address  = await AddAsync(NewAddress());
        var customer = await _svc.AddCustomerAsync(NewCustomer(address));

        customer.Name = "Alice Jones";
        await _svc.SaveCustomerAsync(customer);

        var loaded = await Db.Query<Customer>()
            .FirstOrDefaultAsync(c => c.Id == customer.Id);

        loaded!.Name.Should().Be("Alice Jones");
        loaded.ModifiedBy.Should().Be(TestUser);
        loaded.ModifiedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task DeleteCustomer_HardDeletes_RowIsGone()
    {
        var address  = await AddAsync(NewAddress());
        var customer = await _svc.AddCustomerAsync(NewCustomer(address));
        var id       = customer.Id;

        await _svc.DeleteCustomerAsync(id);

        var loaded = await Db.Query<Customer>()
            .FirstOrDefaultAsync(c => c.Id == id);

        loaded.Should().BeNull("Customer has no ISoftDelete — row permanently deleted");
    }

    [Fact]
    public async Task DeleteCustomer_Throws_WhenNotFound()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _svc.DeleteCustomerAsync(99999));
    }

    [Fact]
    public async Task GetCustomer_ReturnsNull_WhenNotFound()
    {
        var result = await _svc.GetCustomerAsync(99999);
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAllCustomers_ReturnsOrderedByName()
    {
        var address = await AddAsync(NewAddress());
        await _svc.AddCustomerAsync(NewCustomer(address, name: "Zelda"));
        await _svc.AddCustomerAsync(NewCustomer(address, name: "Alice"));
        await _svc.AddCustomerAsync(NewCustomer(address, name: "Mike"));

        var all = await _svc.GetAllCustomersAsync();

        all.Select(c => c.Name)
           .Should().BeInAscendingOrder();
    }
}

// ---------------------------------------------------------------------------
// Order — IAuditable + ISoftDelete, full lifecycle
// ---------------------------------------------------------------------------

public class OrderIntegrationTests : StoreDbFixture
{
    private readonly OrderService _svc;
    private Customer _customer = null!;

    public OrderIntegrationTests()
        => _svc = new OrderService(Db, User);

    protected override async Task SeedAsync()
    {
        var address = await AddAsync(NewAddress());
        _customer   = await AddAsync(NewCustomer(address));
    }

    [Fact]
    public async Task AddOrder_DefaultSoftDeleteValues()
    {
        var order = await _svc.AddOrderAsync(NewOrder(_customer));

        var loaded = await Db.Query<Order>()
            .FirstOrDefaultAsync(o => o.Id == order.Id);

        loaded!.IsDeleted.Should().BeFalse();
        loaded.DeletedAt.Should().BeNull();
        loaded.DeletedBy.Should().BeNull();
    }

    [Fact]
    public async Task DeleteOrder_SoftDeletes_RowRetained()
    {
        var order = await _svc.AddOrderAsync(NewOrder(_customer));
        var id    = order.Id;

        await _svc.DeleteOrderAsync(id);

        // Row must still exist in the database
        var raw = await Db.Query<Order>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(o => o.Id == id);

        raw.Should().NotBeNull("soft-deleted row is retained");
        raw!.IsDeleted.Should().BeTrue();
        raw.DeletedBy.Should().Be(TestUser);
        raw.DeletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task DeleteOrder_HidesFromNormalQuery()
    {
        var order = await _svc.AddOrderAsync(NewOrder(_customer));
        await _svc.DeleteOrderAsync(order.Id);

        // Global filter (WHERE is_deleted = 0) should hide it
        var visible = await _svc.GetOrderAsync(order.Id);
        visible.Should().BeNull("global query filter hides soft-deleted rows");
    }

    [Fact]
    public async Task GetDeletedOrders_ReturnsOnlySoftDeletedRows()
    {
        var active  = await _svc.AddOrderAsync(NewOrder(_customer));
        var deleted = await _svc.AddOrderAsync(NewOrder(_customer));
        await _svc.DeleteOrderAsync(deleted.Id);

        var results = await _svc.GetDeletedOrdersAsync(_customer.Id);

        results.Should().ContainSingle(o => o.Id == deleted.Id);
        results.Should().NotContain(o => o.Id == active.Id);
    }

    [Fact]
    public async Task RestoreOrder_MakesRowVisibleAgain()
    {
        var order = await _svc.AddOrderAsync(NewOrder(_customer));
        await _svc.DeleteOrderAsync(order.Id);
        await _svc.RestoreOrderAsync(order.Id);

        var restored = await _svc.GetOrderAsync(order.Id);

        restored.Should().NotBeNull();
        restored!.IsDeleted.Should().BeFalse();
        restored.DeletedAt.Should().BeNull();
        restored.DeletedBy.Should().BeNull();
    }

    [Fact]
    public async Task PurgeOrder_PermanentlyRemovesRow()
    {
        var order = await _svc.AddOrderAsync(NewOrder(_customer));
        await _svc.DeleteOrderAsync(order.Id);
        await _svc.PurgeOrderAsync(order.Id);

        var gone = await Db.Query<Order>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(o => o.Id == order.Id);

        gone.Should().BeNull("purge issues a physical DELETE");
    }

    [Fact]
    public async Task PurgeOrder_Throws_WhenOrderIsStillActive()
    {
        var order = await _svc.AddOrderAsync(NewOrder(_customer));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _svc.PurgeOrderAsync(order.Id));

        ex.Message.Should().Contain("still active");
    }

    [Fact]
    public async Task DeleteOrder_AlsoStampsIAuditableModifiedFields()
    {
        var order = await _svc.AddOrderAsync(NewOrder(_customer));
        await _svc.DeleteOrderAsync(order.Id);

        var raw = await Db.Query<Order>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(o => o.Id == order.Id);

        // Soft delete triggers Modified state → AuditInterceptor stamps fields
        raw!.ModifiedBy.Should().Be(TestUser);
        raw.ModifiedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task RestoreOrder_Throws_WhenOrderNotFound()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _svc.RestoreOrderAsync(99999));
    }
}

// ---------------------------------------------------------------------------
// Product — ISoftDelete + [Precision] decimal
// ---------------------------------------------------------------------------

public class ProductIntegrationTests : StoreDbFixture
{
    private readonly ProductService _svc;
    private Category _category = null!;

    public ProductIntegrationTests()
        => _svc = new ProductService(Db, User);

    protected override async Task SeedAsync()
        => _category = await AddAsync(NewCategory());

    [Fact]
    public async Task AddProduct_PreservesDecimalPrecision()
    {
        var product = NewProduct(_category, price: 1234.56m);
        product.CostPrice = 999.99m;
        await _svc.AddProductAsync(product);

        var loaded = await _svc.GetProductAsync(product.Id);

        loaded!.Price    .Should().Be(1234.56m);
        loaded.CostPrice.Should().Be(999.99m);
    }

    [Fact]
    public async Task DeleteProduct_SoftDeletes_HiddenFromNormalQuery()
    {
        var product = await _svc.AddProductAsync(NewProduct(_category));
        await _svc.DeleteProductAsync(product.Id);

        var active = await _svc.GetProductAsync(product.Id);
        active.Should().BeNull("soft-deleted product hidden by global filter");

        var deleted = await _svc.GetDeletedProductsAsync();
        deleted.Should().ContainSingle(p => p.Id == product.Id);
    }

    [Fact]
    public async Task RestoreProduct_MakesProductVisibleAgain()
    {
        var product = await _svc.AddProductAsync(NewProduct(_category));
        await _svc.DeleteProductAsync(product.Id);
        await _svc.RestoreProductAsync(product.Id);

        var restored = await _svc.GetProductAsync(product.Id);
        restored.Should().NotBeNull();
    }

    [Fact]
    public async Task AddProduct_Throws_WhenPriceIsZero()
    {
        var product = NewProduct(_category, price: 0m);
        await Assert.ThrowsAsync<ArgumentException>(
            () => _svc.AddProductAsync(product));
    }

    [Fact]
    public async Task GetProductsByCategory_ExcludesSoftDeleted()
    {
        var product1 = await _svc.AddProductAsync(NewProduct(_category, "Active"));
        var product2 = await _svc.AddProductAsync(NewProduct(_category, "ToDelete"));
        await _svc.DeleteProductAsync(product2.Id);

        var results = await _svc.GetProductsByCategoryAsync(_category.Id);

        results.Should().ContainSingle(p => p.Id == product1.Id);
        results.Should().NotContain(p => p.Id == product2.Id);
    }
}

// ---------------------------------------------------------------------------
// ProductReview — optional FK (nullable CustomerId)
// ---------------------------------------------------------------------------

public class ProductReviewIntegrationTests : StoreDbFixture
{
    private readonly ProductReviewService _svc;
    private Product  _product  = null!;
    private Customer _customer = null!;

    public ProductReviewIntegrationTests()
        => _svc = new ProductReviewService(Db, User);

    protected override async Task SeedAsync()
    {
        var address   = await AddAsync(NewAddress());
        _customer     = await AddAsync(NewCustomer(address));
        var category  = await AddAsync(NewCategory());
        _product      = await AddAsync(NewProduct(category));
    }

    [Fact]
    public async Task AddReview_WithCustomer_CanBeRetrievedByCustomerId()
    {
        var review = await _svc.AddReviewAsync(
            NewReview(_product, customerId: _customer.Id));

        var results = await _svc.GetReviewsByCustomerAsync(_customer.Id);
        results.Should().ContainSingle(r => r.Id == review.Id);
    }

    [Fact]
    public async Task AddReview_WithoutCustomer_IsAnonymous()
    {
        var review = await _svc.AddReviewAsync(
            NewReview(_product, customerId: null, comment: "Anonymous review"));

        var anonymous = await _svc.GetReviewsByCustomerAsync(null);
        anonymous.Should().ContainSingle(r => r.Id == review.Id,
            "reviews with null CustomerId are anonymous");
    }

    [Fact]
    public async Task AnonymousReviews_AreNotReturnedForCustomerQuery()
    {
        await _svc.AddReviewAsync(NewReview(_product, customerId: null));

        var results = await _svc.GetReviewsByCustomerAsync(_customer.Id);
        results.Should().BeEmpty("customer query should not return anonymous reviews");
    }

    [Fact]
    public async Task AddReview_Throws_WhenRatingOutOfRange()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _svc.AddReviewAsync(NewReview(_product, rating: 6)));

        await Assert.ThrowsAsync<ArgumentException>(
            () => _svc.AddReviewAsync(NewReview(_product, rating: 0)));
    }

    [Fact]
    public async Task DeleteReview_HardDeletes_ReviewHasNoISoftDelete()
    {
        var review = await _svc.AddReviewAsync(NewReview(_product));
        var id     = review.Id;

        await _svc.DeleteReviewAsync(id);

        var loaded = await Db.Query<ProductReview>()
            .FirstOrDefaultAsync(r => r.Id == id);

        loaded.Should().BeNull(
            "ProductReview has no ISoftDelete — DeleteAsync takes the hard delete path");
    }

    [Fact]
    public async Task GetReviewsForProduct_ReturnsAllReviews()
    {
        await _svc.AddReviewAsync(NewReview(_product, customerId: _customer.Id));
        await _svc.AddReviewAsync(NewReview(_product, customerId: null));

        var results = await _svc.GetReviewsForProductAsync(_product.Id);
        results.Should().HaveCount(2);
    }
}

// ---------------------------------------------------------------------------
// AuditInterceptor — stamps correct fields at correct times
// ---------------------------------------------------------------------------

public class AuditInterceptorTests : StoreDbFixture
{
    private readonly CustomerService _svc;
    public AuditInterceptorTests() => _svc = new CustomerService(Db, User);

    [Fact]
    public async Task Insert_StampsCreatedFields_NotModifiedFields()
    {
        var address  = await AddAsync(NewAddress());
        var customer = await _svc.AddCustomerAsync(NewCustomer(address));

        customer.CreatedBy .Should().Be(TestUser);
        customer.CreatedAt .Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        customer.ModifiedBy.Should().BeNull("not modified yet");
        customer.ModifiedAt.Should().BeNull("not modified yet");
    }

    [Fact]
    public async Task Update_StampsModifiedFields_NotCreatedFields()
    {
        var address  = await AddAsync(NewAddress());
        var customer = await _svc.AddCustomerAsync(NewCustomer(address));

        var originalCreatedAt = customer.CreatedAt;
        var originalCreatedBy = customer.CreatedBy;

        customer.Name = "Updated Name";
        await _svc.SaveCustomerAsync(customer);

        customer.ModifiedBy.Should().Be(TestUser);
        customer.ModifiedAt.Should().NotBeNull();
        customer.CreatedAt .Should().Be(originalCreatedAt, "CreatedAt never changes");
        customer.CreatedBy .Should().Be(originalCreatedBy, "CreatedBy never changes");
    }

    [Fact]
    public async Task AuditInterceptor_Constructor_Throws_WhenUserServiceIsNull()
    {
        var act = () => new AuditInterceptor(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
