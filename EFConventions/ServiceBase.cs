// =============================================================================
// EFConventions — Version 2.1
// Services/ServiceBase.cs
//
// Moved into the library in v2.1 (was application code in v2.0).
//
// Now that ICurrentUserService lives in the EFConventions namespace,
// ServiceBase can ship as part of the library. Consuming applications
// inherit from it without writing any delete/restore/purge logic themselves.
//
// Library boundary summary after v2.1:
//   Library  — ICurrentUserService, ServiceBase<TEntity>, IUnitOfWork,
//               UnitOfWork, AuditInterceptor, EntityConventionBuilder,
//               IEntityBase, IEntity, IAuditable, ISoftDelete
//   App code — ICurrentUserService implementations (HttpContext, System),
//               concrete DbContext (StoreDb), domain entities, app services
// =============================================================================

namespace EFConvention;

/// <summary>
/// Abstract base for all application service classes. Ships as part of the
/// EFConventions library so consuming applications get delete/restore/purge
/// behaviour for free without writing any of this logic themselves.
///
/// <para>
/// Holds <see cref="IUnitOfWork"/> and <see cref="ICurrentUserService"/> and
/// provides three protected helpers:
/// </para>
///
/// <list type="bullet">
///   <item><description>
///     <see cref="DeleteAsync"/> — automatically chooses soft or hard delete
///     at runtime based on whether the entity implements
///     <see cref="ISoftDelete"/>. Adding or removing the interface from an
///     entity type silently flips the delete path with no service code changes.
///   </description></item>
///   <item><description>
///     <see cref="RestoreAsync"/> — reverses a soft delete. Throws if the
///     entity does not implement <see cref="ISoftDelete"/>.
///   </description></item>
///   <item><description>
///     <see cref="PurgeAsync"/> — permanently removes a soft-deleted row.
///     Enforces the two-step pattern: the entity must already be soft-deleted.
///   </description></item>
/// </list>
///
/// <para>
/// <b>Delete path selection in detail:</b>
/// <list type="bullet">
///   <item><description>
///     Entity implements <see cref="ISoftDelete"/> →
///     <b>soft delete</b>: sets <c>IsDeleted = true</c>, stamps
///     <c>DeletedAt</c> / <c>DeletedBy</c> from <see cref="ICurrentUserService"/>,
///     saves. Row is retained and hidden by the global query filter.
///   </description></item>
///   <item><description>
///     Entity does not implement <see cref="ISoftDelete"/> →
///     <b>hard delete</b>: calls <see cref="IUnitOfWork.Remove{TEntity}"/>,
///     saves. Row is permanently removed.
///   </description></item>
/// </list>
/// </para>
/// </summary>
/// <typeparam name="TEntity">
/// The primary entity type managed by the subclass. Must implement
/// <see cref="IEntityBase"/> so the convention builder can discover it.
/// </typeparam>
/// <example>
/// <code>
/// // Application service — inherits from library ServiceBase:
/// public sealed class OrderService : ServiceBase&lt;Order&gt;, IOrderService
/// {
///     public OrderService(IUnitOfWork uow, ICurrentUserService user)
///         : base(uow, user) { }
///
///     public async Task DeleteOrderAsync(int id, CancellationToken ct = default)
///     {
///         var order = await UnitOfWork.Query&lt;Order&gt;()
///             .FirstOrDefaultAsync(o => o.Id == id, ct)
///             ?? throw new KeyNotFoundException($"Order {id} not found.");
///
///         // Order implements ISoftDelete → soft delete chosen automatically.
///         // Remove ISoftDelete from Order and this hard-deletes instead.
///         await DeleteAsync(order, ct);
///     }
/// }
/// </code>
/// </example>
public abstract class ServiceBase<TEntity> where TEntity : class, IEntityBase
{
    /// <summary>
    /// The unit of work used by all derived services. Exposes
    /// <c>Query</c>, <c>Add</c>, <c>Update</c>, <c>Remove</c>,
    /// <c>FindAsync</c>, <c>RefreshAsync</c>, and <c>CompleteAsync</c>.
    /// </summary>
    protected readonly IUnitOfWork UnitOfWork;

    /// <summary>
    /// The current user identity. Used by <see cref="DeleteAsync"/> to stamp
    /// <see cref="ISoftDelete.DeletedBy"/>. Audit fields
    /// (<see cref="IAuditable.CreatedBy"/> / <see cref="IAuditable.ModifiedBy"/>)
    /// are stamped separately by <see cref="Data.AuditInterceptor"/>.
    /// </summary>
    protected readonly ICurrentUserService CurrentUser;

    /// <summary>
    /// Initialises the service base. Both dependencies are injected by the DI
    /// container — register them before resolving any service.
    /// </summary>
    /// <param name="unitOfWork">The scoped unit of work for this request.</param>
    /// <param name="currentUser">
    /// Resolves the current user's identity for soft-delete stamping.
    /// </param>
    protected ServiceBase(IUnitOfWork unitOfWork, ICurrentUserService currentUser)
    {
        UnitOfWork  = unitOfWork  ?? throw new ArgumentNullException(nameof(unitOfWork));
        CurrentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
    }

    // -------------------------------------------------------------------------
    // DeleteAsync — runtime ISoftDelete detection
    // -------------------------------------------------------------------------

    /// <summary>
    /// Deletes <paramref name="entity"/>, choosing the correct path at runtime:
    /// <list type="bullet">
    ///   <item><description>
    ///     <b>Soft delete</b> when entity implements <see cref="ISoftDelete"/>:
    ///     sets <c>IsDeleted = true</c>, stamps <c>DeletedAt</c> and
    ///     <c>DeletedBy</c>, then calls <see cref="IUnitOfWork.CompleteAsync"/>.
    ///     The row is retained. The global EF query filter hides it from all
    ///     normal queries automatically.
    ///   </description></item>
    ///   <item><description>
    ///     <b>Hard delete</b> when entity does not implement <see cref="ISoftDelete"/>:
    ///     calls <see cref="IUnitOfWork.Remove{TEntity}"/> and commits.
    ///     The row is permanently removed.
    ///   </description></item>
    /// </list>
    /// </summary>
    /// <param name="entity">The loaded entity instance to delete.</param>
    /// <param name="ct">Optional cancellation token.</param>
    protected async Task DeleteAsync(TEntity entity, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entity);

        if (entity is ISoftDelete softDeletable)
        {
            // ── Soft delete ───────────────────────────────────────────────────
            // Row is retained. AuditInterceptor also stamps ModifiedAt/ModifiedBy
            // (if IAuditable) because EF sees this as EntityState.Modified.
            softDeletable.IsDeleted = true;
            softDeletable.DeletedAt = DateTime.UtcNow;
            softDeletable.DeletedBy = CurrentUser.UserName ?? "system";
        }
        else
        {
            // ── Hard delete ───────────────────────────────────────────────────
            // Row is permanently removed on CompleteAsync.
            UnitOfWork.Remove(entity);
        }

        await UnitOfWork.CompleteAsync(ct);
    }

    // -------------------------------------------------------------------------
    // RestoreAsync — reverse a soft delete
    // -------------------------------------------------------------------------

    /// <summary>
    /// Clears all <see cref="ISoftDelete"/> fields on <paramref name="entity"/>,
    /// making it visible to normal queries again after
    /// <see cref="IUnitOfWork.CompleteAsync"/> completes.
    /// </summary>
    /// <param name="entity">A soft-deleted entity instance to restore.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <typeparamref name="TEntity"/> does not implement
    /// <see cref="ISoftDelete"/>. Hard-deleted rows cannot be restored.
    /// </exception>
    protected async Task RestoreAsync(TEntity entity, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entity);

        if (entity is not ISoftDelete softDeletable)
            throw new InvalidOperationException(
                $"{typeof(TEntity).Name} does not implement ISoftDelete " +
                "and cannot be restored. Hard-deleted rows are permanent.");

        softDeletable.IsDeleted = false;
        softDeletable.DeletedAt = null;
        softDeletable.DeletedBy = null;

        // AuditInterceptor stamps ModifiedAt/ModifiedBy automatically.
        await UnitOfWork.CompleteAsync(ct);
    }

    // -------------------------------------------------------------------------
    // PurgeAsync — permanent removal of a soft-deleted row
    // -------------------------------------------------------------------------

    /// <summary>
    /// Permanently removes a row that has already been soft-deleted.
    /// Enforces a two-step pattern: the entity must be soft-deleted first,
    /// giving oversight processes a window to intervene before data is
    /// permanently gone.
    /// </summary>
    /// <param name="entity">
    /// The entity to purge. Must implement <see cref="ISoftDelete"/> and have
    /// <c>IsDeleted = true</c>.
    /// </param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <typeparamref name="TEntity"/> does not implement
    /// <see cref="ISoftDelete"/>, or when the entity is still active
    /// (<c>IsDeleted = false</c>) — soft-delete it first.
    /// </exception>
    protected async Task PurgeAsync(TEntity entity, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entity);

        if (entity is not ISoftDelete softDeletable)
            throw new InvalidOperationException(
                $"{typeof(TEntity).Name} does not implement ISoftDelete. " +
                "Use DeleteAsync to remove this entity.");

        if (!softDeletable.IsDeleted)
            throw new InvalidOperationException(
                $"{typeof(TEntity).Name} with Id {(entity as IEntity)?.Id} " +
                "is still active. Call DeleteAsync first before purging.");

        UnitOfWork.Remove(entity);
        await UnitOfWork.CompleteAsync(ct);
    }
}
