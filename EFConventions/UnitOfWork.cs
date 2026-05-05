// =============================================================================
// EFConventions — Version 2.1
// Data/UnitOfWork.cs
//
// Abstract EF Core DbContext implementing IUnitOfWork.
// Deliberately free of ICurrentUserService — audit stamping is handled
// by AuditInterceptor, registered by the concrete subclass via
// DbContextOptionsBuilder.AddInterceptors(). UnitOfWork knows nothing
// about who the current user is.
// =============================================================================

using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace EFConvention;

/// <summary>
/// Abstract EF Core DbContext that implements <see cref="IUnitOfWork"/>.
/// Subclass once per database, supply the domain assembly and convention
/// configuration, and register an <see cref="AuditInterceptor"/> via
/// <c>OnConfiguring</c> if audit stamping is required.
///
/// <para>
/// <see cref="UnitOfWork"/> has no dependency on <c>ICurrentUserService</c>.
/// Audit stamping is a cross-cutting concern handled entirely by
/// <see cref="AuditInterceptor"/>, which is registered on the
/// <c>DbContextOptions</c> by the concrete subclass. This keeps the data
/// access layer focused solely on persistence.
/// </para>
/// </summary>
/// <example>
/// <code>
/// public sealed class StoreDb : UnitOfWork
/// {
///     private readonly string _cs;
///     private readonly AuditInterceptor _audit;
///
///     public StoreDb(string connectionString, AuditInterceptor audit)
///         : base(typeof(Customer).Assembly,
///                b => b.UseSnakeCase().WithFullAudit())
///     {
///         _cs    = connectionString;
///         _audit = audit;
///     }
///
///     protected override void OnConfiguring(DbContextOptionsBuilder options)
///     {
///         if (!options.IsConfigured)
///             options.UseSqlServer(_cs).AddInterceptors(_audit);
///     }
/// }
/// </code>
/// </example>
public abstract class UnitOfWork : DbContext, IUnitOfWork
{
    private readonly EntityConventionBuilder _conventions;

    /// <summary>
    /// Initialises the unit of work.
    /// </summary>
    /// <param name="domainAssembly">
    /// Assembly containing all <see cref="IEntityBase"/> domain types.
    /// </param>
    /// <param name="configureConventions">
    /// Delegate to configure the <see cref="EntityConventionBuilder"/>.
    /// If omitted, defaults to <c>UseSnakeCase().WithFullAudit()</c>.
    /// </param>
    protected UnitOfWork(
        Assembly domainAssembly,
        Action<EntityConventionBuilder>? configureConventions = null)
    {
        _conventions = EntityConventionBuilder.ForAssembly(domainAssembly);

        if (configureConventions != null)
            configureConventions(_conventions);
        else
            _conventions.UseSnakeCase().WithFullAudit();
    }

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        _conventions.Apply(modelBuilder);
        base.OnModelCreating(modelBuilder);
    }

    // -------------------------------------------------------------------------
    // IUnitOfWork — transaction boundary
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public async Task CompleteAsync(CancellationToken ct = default) =>
        await SaveChangesAsync(ct);

    /// <inheritdoc/>
    public void Complete() => SaveChanges();

    // -------------------------------------------------------------------------
    // IUnitOfWork — query / commands / lookup
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public IQueryable<TEntity> Query<TEntity>() where TEntity : class =>
        Set<TEntity>();

    /// <inheritdoc/>
    //public void Add<TEntity>(TEntity entity) where TEntity : class =>
    //    Set<TEntity>().Add(entity);
    void IUnitOfWork.Add<TEntity>(TEntity entity)
        where TEntity : class => Set<TEntity>().Add(entity);

    ///// <inheritdoc/>
    //public void Update<TEntity>(TEntity entity) where TEntity : class =>
    //    Set<TEntity>().Update(entity);
    void IUnitOfWork.Update<TEntity>(TEntity entity)
        where TEntity : class => Set<TEntity>().Update(entity);

    ///// <inheritdoc/>
    //public void Remove<TEntity>(TEntity entity) where TEntity : class =>
    //    Set<TEntity>().Remove(entity);
    void IUnitOfWork.Remove<TEntity>(TEntity entity)
        where TEntity : class => Set<TEntity>().Remove(entity);

    /// <inheritdoc/>
    public async ValueTask<TEntity?> FindAsync<TEntity>(int id, CancellationToken ct = default)
        where TEntity : class =>
        await Set<TEntity>().FindAsync(new object[] { id }, ct);

    // -------------------------------------------------------------------------
    // IUnitOfWork — refresh
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public async Task RefreshAsync<TEntity>(
        TEntity entity,
        params Expression<Func<TEntity, object>>[] references)
        where TEntity : class
    {
        var entry = Entry(entity);
        await entry.ReloadAsync();
        await LoadReferencesAsync(entry, references);
    }

    /// <inheritdoc/>
    public async Task RefreshCollectionAsync<TEntity, TElement>(
    TEntity entity,
    Expression<Func<TEntity, ICollection<TElement>>> collection,
    CancellationToken ct = default)
    where TEntity : class
    where TElement : class
    {
        var memberName = ((MemberExpression)collection.Body).Member.Name;
        await Entry(entity).Collection(memberName).LoadAsync(ct);
    }

    private async Task LoadReferencesAsync<TEntity>(
        Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<TEntity> entry,
        IEnumerable<Expression<Func<TEntity, object>>> references)
        where TEntity : class
    {
        foreach (var expression in references)
        {
            var parameter = expression.Parameters.Single();
            if (expression.Body == parameter)
                await LoadAllReferencesAsync(entry, parameter);
            else
                await LoadReferenceAsync(entry, expression);
        }
    }

    private static async Task LoadAllReferencesAsync<TEntity>(
        Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<TEntity> entry,
        ParameterExpression parameter)
        where TEntity : class
    {
        var refProps = typeof(TEntity)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite &&
                        typeof(IEntityBase).IsAssignableFrom(p.PropertyType));

        foreach (var prop in refProps)
        {
            var body = Expression.Convert(
                Expression.Property(parameter, prop), typeof(object));
            var expr = Expression.Lambda<Func<TEntity, object>>(body, parameter);
            await LoadReferenceAsync(entry, expr);
        }
    }

    private static async Task LoadReferenceAsync<TEntity>(
        Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<TEntity> entry,
        Expression<Func<TEntity, object>> expression)
        where TEntity : class
    {
        if (expression.Body is MemberExpression mem ||
            (expression.Body is UnaryExpression u &&
             u.Operand is MemberExpression mem2 &&
             (mem = mem2) != null))
        {
            await entry.Reference(mem.Member.Name).LoadAsync();
        }
    }
}
