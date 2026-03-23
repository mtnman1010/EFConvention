// =============================================================================
// EFConventions — Version 2.1
// Core/DomainContracts.cs
//
// Core contracts for the EFConventions library:
//
//   ICurrentUserService   — [NEW in 2.1] moved here from the application layer
//                           so ServiceBase, AuditInterceptor, and all library
//                           code can reference it without a circular dependency.
// =============================================================================

namespace EFConventions;

// -----------------------------------------------------------------------------
// ICurrentUserService — moved into the library in v2.1
// -----------------------------------------------------------------------------

/// <summary>
/// Abstracts the current user's identity. Defined in the library so that
/// <see cref="Data.AuditInterceptor"/> and
/// <see cref="Services.ServiceBase{TEntity}"/> can both reference it without
/// a circular dependency on the consuming application.
///
/// <para>
/// Implement this interface once in your application layer and register the
/// implementation in DI. Two implementations ship as examples in
/// <c>StoreDb.cs</c>: <c>HttpContextCurrentUserService</c> for ASP.NET Core
/// web apps, and <c>SystemUserService</c> for background jobs and console apps.
/// </para>
/// </summary>
/// <example>
/// <code>
/// // ASP.NET Core web app:
/// public class HttpContextCurrentUserService : ICurrentUserService
/// {
///     private readonly IHttpContextAccessor _accessor;
///     public HttpContextCurrentUserService(IHttpContextAccessor accessor)
///         => _accessor = accessor;
///     public string? UserName => _accessor.HttpContext?.User?.Identity?.Name;
/// }
///
/// // Background job / console:
/// public class SystemUserService : ICurrentUserService
/// {
///     public string? UserName => "system";
/// }
/// </code>
/// </example>
public interface ICurrentUserService
{
    /// <summary>
    /// The username of the currently authenticated user, or <c>null</c>
    /// if the context is unauthenticated. Falls back to <c>"system"</c>
    /// at all usage sites within the library.
    /// </summary>
    string? UserName { get; }
}
