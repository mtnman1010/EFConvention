// =============================================================================
// EFConventions — Version 2.1
// Data/AuditInterceptor.cs
//
// EF Core SaveChangesInterceptor that stamps IAuditable fields before every
// save. Registered via DbContextOptionsBuilder.AddInterceptors() in the
// concrete database class (e.g. StoreDb) — UnitOfWork never sees it.
//
// Change from v2.0:
//   ICurrentUserService is now in the EFConventions namespace (library)
//   rather than EFConventions.Sample (application). The using directive
//   below is the only change from the v2.0 file.
// =============================================================================

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EFConvention;

/// <summary>
/// EF Core <see cref="SaveChangesInterceptor"/> that stamps
/// <see cref="IAuditable"/> fields on every save. Ships as part of the
/// EFConventions library — register it in your concrete database class via
/// <c>DbContextOptionsBuilder.AddInterceptors()</c>.
///
/// <para>
/// <see cref="ICurrentUserService"/> is defined in the library (see
/// <c>DomainContracts.cs</c>). Implement it once in your application and
/// register it in DI — this interceptor will resolve it automatically.
/// </para>
/// </summary>
/// <example>
/// <code>
/// // In your concrete DbContext (application code):
/// protected override void OnConfiguring(DbContextOptionsBuilder options)
/// {
///     options.UseSqlServer(_connectionString)
///            .AddInterceptors(new AuditInterceptor(_currentUserService));
/// }
/// </code>
/// </example>
public sealed class AuditInterceptor : SaveChangesInterceptor
{
    private readonly ICurrentUserService _currentUser;

    /// <summary>
    /// Creates a new interceptor bound to <paramref name="currentUser"/>.
    /// Falls back to <c>"system"</c> when <c>UserName</c> is null.
    /// </summary>
    public AuditInterceptor(ICurrentUserService currentUser)
    {
        _currentUser = currentUser
            ?? throw new ArgumentNullException(nameof(currentUser));
    }

    // -------------------------------------------------------------------------
    // Synchronous path
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        Stamp(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    // -------------------------------------------------------------------------
    // Async path
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Stamp(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    // -------------------------------------------------------------------------
    // Stamping logic
    // -------------------------------------------------------------------------

    private void Stamp(DbContext? context)
    {
        if (context is null) return;

        var user = _currentUser.UserName ?? "system";
        var now  = DateTime.UtcNow;

        foreach (var entry in context.ChangeTracker.Entries<IAuditable>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt  = now;
                    entry.Entity.CreatedBy  = user;
                    break;

                case EntityState.Modified:
                    entry.Entity.ModifiedAt = now;
                    entry.Entity.ModifiedBy = user;
                    break;
            }
        }
    }
}
