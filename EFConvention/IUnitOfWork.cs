// =============================================================================
// EFConventions — Version 2.1
// Data/IUnitOfWork.cs
//
// The Unit of Work interface defines the data access contract consumed by
// all service objects. Keeping this interface free of any EF Core types means
// services can be tested with a mock or in-memory implementation without
// needing EF infrastructure in unit tests.
// =============================================================================

using System.Linq.Expressions;

namespace EFConvention;

/// <summary>
/// Abstracts the data access layer. All service objects depend on this
/// interface rather than <see cref="UnitOfWork"/> directly, enabling clean
/// unit tests with a mock implementation.
///
/// <para>
/// Call <see cref="CompleteAsync"/> (or <see cref="Complete"/> for synchronous
/// callers) to commit all pending changes. A single unit of work typically
/// spans one HTTP request or one background job execution.
/// </para>
/// </summary>
public interface IUnitOfWork : IDisposable
{
    // -------------------------------------------------------------------------
    // Transaction boundary
    // -------------------------------------------------------------------------

    /// <summary>
    /// Commits all pending changes to the database asynchronously.
    /// Equivalent to <c>SaveChangesAsync</c> but named to make the
    /// unit-of-work boundary explicit at the call site.
    /// </summary>
    Task CompleteAsync(CancellationToken ct = default);

    /// <summary>
    /// Commits all pending changes to the database synchronously.
    /// Prefer <see cref="CompleteAsync"/> in async contexts.
    /// </summary>
    void Complete();

    // -------------------------------------------------------------------------
    // Query
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns a composable <see cref="IQueryable{TEntity}"/> for
    /// <typeparamref name="TEntity"/>. Callers add LINQ predicates, projections,
    /// and ordering before materialising the query.
    /// </summary>
    /// <example>
    /// <code>
    /// var activeOrders = unitOfWork
    ///     .Query&lt;Order&gt;()
    ///     .Where(o => o.CustomerId == customerId)
    ///     .OrderByDescending(o => o.OrderDate)
    ///     .ToListAsync();
    /// </code>
    /// </example>
    IQueryable<TEntity> Query<TEntity>() where TEntity : class;

    // -------------------------------------------------------------------------
    // Commands
    // -------------------------------------------------------------------------

    /// <summary>Stages <paramref name="entity"/> for INSERT on the next <see cref="CompleteAsync"/>.</summary>
    void Add<TEntity>(TEntity entity) where TEntity : class;

    /// <summary>Stages <paramref name="entity"/> for UPDATE on the next <see cref="CompleteAsync"/>.</summary>
    void Update<TEntity>(TEntity entity) where TEntity : class;

    /// <summary>
    /// Stages <paramref name="entity"/> for DELETE on the next
    /// <see cref="CompleteAsync"/>. For <see cref="ISoftDelete"/> entities use
    /// the service layer's <c>DeleteAsync</c> instead — this method always
    /// issues a physical DELETE.
    /// </summary>
    void Remove<TEntity>(TEntity entity) where TEntity : class;

    // -------------------------------------------------------------------------
    // Lookup
    // -------------------------------------------------------------------------

    /// <summary>
    /// Finds an entity by its integer primary key. Returns <c>null</c> if not
    /// found. Uses the EF identity map so a tracked entity is returned without
    /// a database round-trip when already loaded in this context.
    /// </summary>
    ValueTask<TEntity?> FindAsync<TEntity>(int id, CancellationToken ct = default)
        where TEntity : class;

    // -------------------------------------------------------------------------
    // Refresh
    // -------------------------------------------------------------------------

    /// <summary>
    /// Reloads <paramref name="entity"/> from the database, discarding any
    /// in-memory changes, and optionally reloads the specified navigation
    /// properties. Pass the entity parameter itself (<c>e => e</c>) to reload
    /// all reference navigations.
    /// </summary>
    /// <example>
    /// <code>
    /// // Reload one reference:
    /// await unitOfWork.RefreshAsync(order, o => o.Customer);
    ///
    /// // Reload all references:
    /// await unitOfWork.RefreshAsync(order, o => o);
    /// </code>
    /// </example>
    Task RefreshAsync<TEntity>(
        TEntity entity,
        params Expression<Func<TEntity, object>>[] references)
        where TEntity : class;

    /// <summary>
    /// Reloads a collection navigation property from the database, replacing
    /// the in-memory collection with fresh data.
    /// </summary>
    Task RefreshCollectionAsync<TEntity, TElement>(
        TEntity entity,
        Expression<Func<TEntity, ICollection<TElement>>> collection,
        CancellationToken ct = default)
        where TEntity : class
        where TElement : class;
}
