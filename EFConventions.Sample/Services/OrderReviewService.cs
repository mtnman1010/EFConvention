// =============================================================================
// EFConventions — Version 2.1
// Services/OrderService.cs
//
// Order service — IAuditable + ISoftDelete + full soft-delete lifecycle.
// Demonstrates: soft delete, restore, purge, IgnoreQueryFilters.
//
// ProductReviewService — optional FK (nullable CustomerId).
// Demonstrates: optional relationship querying.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EFConventions.Data;
using EFConventions.Domain;
using Microsoft.EntityFrameworkCore;

namespace EFConventions.Services;

// =============================================================================
// IOrderService / OrderService
// =============================================================================

public interface IOrderService
{
    // Active orders (soft-deleted excluded automatically by global filter)
    Task<Order?>               GetOrderAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<Order>> GetOrdersByCustomerAsync(int customerId, CancellationToken ct = default);

    // Soft-deleted orders (opt-in via IgnoreQueryFilters)
    Task<IReadOnlyList<Order>> GetDeletedOrdersAsync(int customerId, CancellationToken ct = default);

    Task<Order> AddOrderAsync(Order order, CancellationToken ct = default);
    Task        SaveOrderAsync(Order order, CancellationToken ct = default);

    /// <summary>
    /// Soft-deletes the order — sets <c>IsDeleted = true</c>, stamps
    /// <c>DeletedAt</c>/<c>DeletedBy</c>. Row retained in database.
    /// Visible only via <see cref="GetDeletedOrdersAsync"/>.
    /// </summary>
    Task DeleteOrderAsync(int id, CancellationToken ct = default);

    /// <summary>Reverses a soft delete — order becomes visible to normal queries again.</summary>
    Task RestoreOrderAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Permanently removes a soft-deleted order. The order must already be
    /// soft-deleted — call <see cref="DeleteOrderAsync"/> first.
    /// </summary>
    Task PurgeOrderAsync(int id, CancellationToken ct = default);
}

public sealed class OrderService : ServiceBase<Order>, IOrderService
{
    public OrderService(IUnitOfWork unitOfWork, ICurrentUserService currentUser)
        : base(unitOfWork, currentUser) { }

    public async Task<Order?> GetOrderAsync(int id, CancellationToken ct = default) =>
        // Global filter (WHERE is_deleted = 0) applied automatically.
        await UnitOfWork.Query<Order>()
            .Include(o => o.Customer)
            .Include(o => o.Items).ThenInclude(i => i.Product)
            .FirstOrDefaultAsync(o => o.Id == id, ct);

    public async Task<IReadOnlyList<Order>> GetOrdersByCustomerAsync(
        int customerId, CancellationToken ct = default) =>
        await UnitOfWork.Query<Order>()
            .Where(o => o.CustomerId == customerId)
            .OrderByDescending(o => o.OrderDate)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Order>> GetDeletedOrdersAsync(
        int customerId, CancellationToken ct = default) =>
        // Must bypass the global filter to reach soft-deleted rows.
        await UnitOfWork.Query<Order>()
            .IgnoreQueryFilters()
            .Where(o => o.CustomerId == customerId && o.IsDeleted)
            .OrderByDescending(o => o.DeletedAt)
            .ToListAsync(ct);

    public async Task<Order> AddOrderAsync(Order order, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(order);
        if (order.OrderDate == default)
            throw new ArgumentException("OrderDate must be set.", nameof(order));

        UnitOfWork.Add(order);
        await UnitOfWork.CompleteAsync(ct);
        return order;
    }

    public async Task SaveOrderAsync(Order order, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(order);
        var exists = await UnitOfWork.Query<Order>()
            .AnyAsync(o => o.Id == order.Id, ct);
        if (!exists)
            throw new KeyNotFoundException(
                $"Active order {order.Id} not found. It may be soft-deleted.");

        UnitOfWork.Update(order);
        await UnitOfWork.CompleteAsync(ct);
    }

    public async Task DeleteOrderAsync(int id, CancellationToken ct = default)
    {
        var order = await UnitOfWork.Query<Order>()
            .FirstOrDefaultAsync(o => o.Id == id, ct)
            ?? throw new KeyNotFoundException(
                $"Active order {id} not found. It may already be soft-deleted.");

        // Order implements ISoftDelete → ServiceBase chooses soft delete path.
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

        // PurgeAsync guards that IsDeleted = true before removing permanently.
        await PurgeAsync(order, ct);
    }
}

// =============================================================================
// IProductReviewService / ProductReviewService
// =============================================================================

/// <summary>
/// Reviews demonstrate an <b>optional</b> FK relationship: <c>CustomerId</c>
/// is <c>int?</c> so reviews can exist even after a customer account is deleted.
/// </summary>
public interface IProductReviewService
{
    Task<IReadOnlyList<ProductReview>> GetReviewsForProductAsync(int productId, CancellationToken ct = default);

    /// <summary>
    /// Returns all reviews by a specific customer, or all anonymous reviews
    /// when <paramref name="customerId"/> is <c>null</c>.
    /// </summary>
    Task<IReadOnlyList<ProductReview>> GetReviewsByCustomerAsync(int? customerId, CancellationToken ct = default);

    Task<ProductReview> AddReviewAsync(ProductReview review, CancellationToken ct = default);

    /// <summary>
    /// Permanently deletes a review. <see cref="ProductReview"/> does not
    /// implement <see cref="ISoftDelete"/> so this is always a hard delete.
    /// </summary>
    Task DeleteReviewAsync(int id, CancellationToken ct = default);
}

public sealed class ProductReviewService : ServiceBase<ProductReview>, IProductReviewService
{
    public ProductReviewService(IUnitOfWork unitOfWork, ICurrentUserService currentUser)
        : base(unitOfWork, currentUser) { }

    public async Task<IReadOnlyList<ProductReview>> GetReviewsForProductAsync(
        int productId, CancellationToken ct = default) =>
        await UnitOfWork.Query<ProductReview>()
            .Include(r => r.Customer)
            .Where(r => r.ProductId == productId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ProductReview>> GetReviewsByCustomerAsync(
        int? customerId, CancellationToken ct = default)
    {
        var query = UnitOfWork.Query<ProductReview>().Include(r => r.Product);

        // Optional FK — query for anonymous reviews OR reviews by a specific customer.
        return customerId.HasValue
            ? await query.Where(r => r.CustomerId == customerId).ToListAsync(ct)
            : await query.Where(r => r.CustomerId == null).ToListAsync(ct);
    }

    public async Task<ProductReview> AddReviewAsync(ProductReview review, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(review);
        if (review.Rating is < 1 or > 5)
            throw new ArgumentException("Rating must be between 1 and 5.", nameof(review));

        UnitOfWork.Add(review);
        await UnitOfWork.CompleteAsync(ct);
        return review;
    }

    public async Task DeleteReviewAsync(int id, CancellationToken ct = default)
    {
        var review = await UnitOfWork.Query<ProductReview>()
            .FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new KeyNotFoundException($"Review {id} not found.");

        // ProductReview has no ISoftDelete → hard delete path chosen automatically.
        await DeleteAsync(review, ct);
    }
}
