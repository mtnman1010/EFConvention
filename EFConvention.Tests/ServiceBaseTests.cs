// =============================================================================
// EFConvention.Tests
// Services/ServiceBaseTests.cs
//
// Unit tests for ServiceBase<TEntity> using Moq to mock IUnitOfWork.
// No EF Core or database involved — pure logic tests.
//
// Suites:
//   DeleteAsyncTests   — soft vs hard delete path selection, stamping, guards
//   RestoreAsyncTests  — clears ISoftDelete fields, throws for non-ISoftDelete
//   PurgeAsyncTests    — permanent removal, two-step guard
// =============================================================================

using EFConventions;
using EFConventions.Domain;
using FluentAssertions;
using Moq;
using Xunit;

namespace EFConvention.Tests.Services;

// ---------------------------------------------------------------------------
// Concrete test services — expose protected helpers as public for testing
// ---------------------------------------------------------------------------

file sealed class TestOrderService : ServiceBase<Order>
{
    public TestOrderService(IUnitOfWork uow, ICurrentUserService user)
        : base(uow, user) { }

    public Task Delete(Order o, CancellationToken ct = default)  => DeleteAsync(o, ct);
    public Task Restore(Order o, CancellationToken ct = default) => RestoreAsync(o, ct);
    public Task Purge(Order o, CancellationToken ct = default)   => PurgeAsync(o, ct);
}

file sealed class TestCustomerService : ServiceBase<Customer>
{
    public TestCustomerService(IUnitOfWork uow, ICurrentUserService user)
        : base(uow, user) { }

    public Task Delete(Customer c, CancellationToken ct = default) => DeleteAsync(c, ct);
}

// ---------------------------------------------------------------------------
// Shared test base
// ---------------------------------------------------------------------------

public abstract class ServiceBaseTestBase
{
    protected readonly Mock<IUnitOfWork>         MockUow;
    protected readonly Mock<ICurrentUserService> MockUser;
    protected const    string                    TestUserName = "unit-test-user";

    protected ServiceBaseTestBase()
    {
        MockUow  = new Mock<IUnitOfWork>();
        MockUser = new Mock<ICurrentUserService>();
        MockUser.Setup(u => u.UserName).Returns(TestUserName);
        MockUow .Setup(u => u.CompleteAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
    }

    protected Order ActiveOrder() => new()
    {
        Id         = 1,
        OrderDate  = DateTime.UtcNow,
        CustomerId = 1,
        IsDeleted  = false
    };

    protected Order SoftDeletedOrder() => new()
    {
        Id         = 2,
        OrderDate  = DateTime.UtcNow.AddDays(-5),
        CustomerId = 1,
        IsDeleted  = true,
        DeletedAt  = DateTime.UtcNow.AddDays(-1),
        DeletedBy  = "admin"
    };

    protected Customer ActiveCustomer() => new()
    {
        Id        = 1,
        Name      = "Alice",
        Email     = "alice@example.com",
        AddressId = 1
    };
}

// ---------------------------------------------------------------------------
// DeleteAsync
// ---------------------------------------------------------------------------

public class DeleteAsyncTests : ServiceBaseTestBase
{
    [Fact]
    public async Task SoftDelete_WhenEntityImplementsISoftDelete()
    {
        var svc   = new TestOrderService(MockUow.Object, MockUser.Object);
        var order = ActiveOrder();

        await svc.Delete(order);

        order.IsDeleted.Should().BeTrue();
        order.DeletedAt.Should().NotBeNull();
        order.DeletedBy.Should().Be(TestUserName);
    }

    [Fact]
    public async Task SoftDelete_DoesNotCallRemove()
    {
        var svc = new TestOrderService(MockUow.Object, MockUser.Object);
        await svc.Delete(ActiveOrder());
        MockUow.Verify(u => u.Remove(It.IsAny<Order>()), Times.Never);
    }

    [Fact]
    public async Task SoftDelete_CallsCompleteAsync()
    {
        var svc = new TestOrderService(MockUow.Object, MockUser.Object);
        await svc.Delete(ActiveOrder());
        MockUow.Verify(u => u.CompleteAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HardDelete_WhenEntityDoesNotImplementISoftDelete()
    {
        var svc      = new TestCustomerService(MockUow.Object, MockUser.Object);
        var customer = ActiveCustomer();

        await svc.Delete(customer);

        MockUow.Verify(u => u.Remove(customer), Times.Once);
    }

    [Fact]
    public async Task HardDelete_CallsCompleteAsync()
    {
        var svc = new TestCustomerService(MockUow.Object, MockUser.Object);
        await svc.Delete(ActiveCustomer());
        MockUow.Verify(u => u.CompleteAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SoftDelete_StampsUtcTimestamp()
    {
        var svc    = new TestOrderService(MockUow.Object, MockUser.Object);
        var order  = ActiveOrder();
        var before = DateTime.UtcNow;

        await svc.Delete(order);

        order.DeletedAt.Should().NotBeNull();
        order.DeletedAt!.Value.Should().BeOnOrAfter(before);
        order.DeletedAt!.Value.Should().BeOnOrBefore(DateTime.UtcNow);
    }

    [Fact]
    public async Task SoftDelete_FallsBackToSystem_WhenUserNameIsNull()
    {
        MockUser.Setup(u => u.UserName).Returns((string?)null);
        var svc   = new TestOrderService(MockUow.Object, MockUser.Object);
        var order = ActiveOrder();

        await svc.Delete(order);

        order.DeletedBy.Should().Be("system");
    }

    [Fact]
    public async Task Throws_WhenEntityIsNull()
    {
        var svc = new TestOrderService(MockUow.Object, MockUser.Object);
        await Assert.ThrowsAsync<ArgumentNullException>(() => svc.Delete(null!));
    }
}

// ---------------------------------------------------------------------------
// RestoreAsync
// ---------------------------------------------------------------------------

public class RestoreAsyncTests : ServiceBaseTestBase
{
    [Fact]
    public async Task ClearsSoftDeleteFields()
    {
        var svc   = new TestOrderService(MockUow.Object, MockUser.Object);
        var order = SoftDeletedOrder();

        await svc.Restore(order);

        order.IsDeleted.Should().BeFalse();
        order.DeletedAt.Should().BeNull();
        order.DeletedBy.Should().BeNull();
    }

    [Fact]
    public async Task CallsCompleteAsync()
    {
        var svc = new TestOrderService(MockUow.Object, MockUser.Object);
        await svc.Restore(SoftDeletedOrder());
        MockUow.Verify(u => u.CompleteAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Throws_WhenEntityIsNull()
    {
        var svc = new TestOrderService(MockUow.Object, MockUser.Object);
        await Assert.ThrowsAsync<ArgumentNullException>(() => svc.Restore(null!));
    }
}

// ---------------------------------------------------------------------------
// PurgeAsync
// ---------------------------------------------------------------------------

public class PurgeAsyncTests : ServiceBaseTestBase
{
    [Fact]
    public async Task CallsRemove_OnSoftDeletedEntity()
    {
        var svc   = new TestOrderService(MockUow.Object, MockUser.Object);
        var order = SoftDeletedOrder();

        await svc.Purge(order);

        MockUow.Verify(u => u.Remove(order), Times.Once);
        MockUow.Verify(u => u.CompleteAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Throws_WhenEntityIsStillActive()
    {
        var svc   = new TestOrderService(MockUow.Object, MockUser.Object);
        var order = ActiveOrder(); // IsDeleted = false

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.Purge(order));

        ex.Message.Should().Contain("still active");
    }

    [Fact]
    public async Task DoesNotCallComplete_WhenEntityIsStillActive()
    {
        var svc = new TestOrderService(MockUow.Object, MockUser.Object);
        try { await svc.Purge(ActiveOrder()); } catch { /* expected */ }
        MockUow.Verify(u => u.CompleteAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Throws_WhenEntityIsNull()
    {
        var svc = new TestOrderService(MockUow.Object, MockUser.Object);
        await Assert.ThrowsAsync<ArgumentNullException>(() => svc.Purge(null!));
    }
}
