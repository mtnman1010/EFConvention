// =============================================================================
// EFConvention — Version 2.3
// Data/StoreDb.cs  [APPLICATION CODE — not part of the library]
//
// Changes from v2.2:
//   Domain objects no longer require scalar FK properties (AddressId,
//   CustomerId etc.). Required/optional relationships are now detected
//   via nullable reference type annotations on the navigation property:
//     public Address  Address  { get; set; } = null!;  // non-nullable → required
//     public Customer? Customer { get; set; }           // nullable    → optional
//
// Contents:
//   HttpContextCurrentUserService — ASP.NET Core web app implementation
//   SystemUserService             — background job / console app implementation
//   Additional implementations    — commented examples for other environments
//   StoreDb                       — concrete UnitOfWork for the Store database
//   ServiceRegistration           — DI extension methods
// =============================================================================

using EFConvention.Domain;
using EFConvention.Sample.Services;
using EFConvention.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EFConvention.Sample;

// -----------------------------------------------------------------------------
// ICurrentUserService implementations  [APPLICATION CODE]
//
// ICurrentUserService is defined in the library (EFConvention namespace).
// Only the implementations live here — one per hosting environment.
//
// ICurrentUserService is ONLY required when using audit stamping via
// WithAuditFields() or WithFullAudit(). If your application does not need
// audit stamping, skip this entirely — see the SimpleDb example below.
// -----------------------------------------------------------------------------

/// <summary>
/// ASP.NET Core MVC / Razor Pages / Web API implementation.
/// Resolves the username from the current HTTP request's claims principal
/// via <see cref="IHttpContextAccessor"/>.
/// </summary>
public sealed class HttpContextCurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _accessor;

    public HttpContextCurrentUserService(IHttpContextAccessor accessor)
        => _accessor = accessor;

    /// <inheritdoc/>
    public string? UserName => _accessor.HttpContext?.User?.Identity?.Name;
}

/// <summary>
/// Implementation for background jobs, scheduled tasks, and console apps
/// where there is no HTTP context. Always returns <c>"system"</c>.
/// </summary>
public sealed class SystemUserService : ICurrentUserService
{
    /// <inheritdoc/>
    public string? UserName => "system";
}

// -----------------------------------------------------------------------------
// Additional ICurrentUserService implementations — uncomment as needed
// -----------------------------------------------------------------------------

// Web API with JWT — resolve from claims principal
//
//public sealed class JwtCurrentUserService : ICurrentUserService
//{
//    private readonly IHttpContextAccessor _accessor;
//    public JwtCurrentUserService(IHttpContextAccessor accessor)
//        => _accessor = accessor;
//    public string? UserName =>
//        _accessor.HttpContext?.User?.FindFirst(
//            System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
//        ?? _accessor.HttpContext?.User?.FindFirst("sub")?.Value;
//}

// Blazor Server — resolve from AuthenticationStateProvider
//
//public sealed class BlazorCurrentUserService : ICurrentUserService
//{
//    private readonly Microsoft.AspNetCore.Components.Authorization
//        .AuthenticationStateProvider _authState;
//    public BlazorCurrentUserService(
//        Microsoft.AspNetCore.Components.Authorization
//            .AuthenticationStateProvider authState)
//        => _authState = authState;
//    public string? UserName
//    {
//        get
//        {
//            var state = _authState.GetAuthenticationStateAsync()
//                .GetAwaiter().GetResult();
//            return state.User?.Identity?.Name;
//        }
//    }
//}

// WPF / WinForms — Windows authentication (domain-joined apps)
//
//public sealed class WindowsCurrentUserService : ICurrentUserService
//{
//    public string? UserName =>
//        System.Security.Principal.WindowsIdentity.GetCurrent().Name;
//}

// WPF / WinForms / MVVM — custom login session
//
//public sealed class SessionCurrentUserService : ICurrentUserService
//{
//    private readonly ISessionService _session;
//    public SessionCurrentUserService(ISessionService session)
//        => _session = session;
//    public string? UserName => _session.CurrentUser?.Username;
//}

// Console / CLI — Environment.UserName or prompt at startup
//
//public sealed class ConsoleCurrentUserService : ICurrentUserService
//{
//    public string? UserName { get; }
//    public ConsoleCurrentUserService(string userName) => UserName = userName;
//}
//
// Usage in Program.cs:
//   var userName = args.FirstOrDefault() ?? Environment.UserName;
//   services.AddSingleton<ICurrentUserService>(
//       new ConsoleCurrentUserService(userName));

// -----------------------------------------------------------------------------
// StoreDb  [APPLICATION CODE]
// -----------------------------------------------------------------------------

/// <summary>
/// The concrete <see cref="UnitOfWork"/> for the Store database.
/// This is the only class in the application that knows:
/// <list type="bullet">
///   <item><description>The domain assembly anchor type (<see cref="Customer"/>).</description></item>
///   <item><description>The database connection string.</description></item>
///   <item><description>Which convention builder options to apply.</description></item>
///   <item><description>
///     How to connect <see cref="ICurrentUserService"/> to the persistence
///     layer — via <see cref="AuditInterceptor"/>, not via
///     <see cref="UnitOfWork"/> itself.
///   </description></item>
/// </list>
///
/// <para>
/// Uses the default PascalCase naming convention — table names, column names,
/// and FK columns all match C# class and property names exactly.
/// To use snake_case instead, change <c>b => b.WithFullAudit()</c> to
/// <c>b => b.UseSnakeCase().WithFullAudit()</c> and update your schema
/// script accordingly.
/// </para>
///
/// <para>
/// The assembly anchor (<c>typeof(Customer)</c>) is any type from your domain
/// assembly — it tells the builder which assembly to scan for
/// <see cref="IEntityBase"/> implementations. It has no relationship to
/// <see cref="ICurrentUserService"/> or audit stamping.
/// </para>
///
/// All other layers depend on <see cref="IUnitOfWork"/> and never reference
/// this class directly.
/// </summary>
public sealed class StoreDb : UnitOfWork
{
    private readonly string _connectionString;
    private readonly AuditInterceptor _auditInterceptor;

    /// <param name="connectionString">SQL Server connection string.</param>
    /// <param name="currentUser">
    /// Injected by DI. Passed to <see cref="AuditInterceptor"/> — the
    /// <see cref="UnitOfWork"/> base class never sees it.
    /// </param>
    public StoreDb(string connectionString, ICurrentUserService currentUser)
        : base(
            domainAssembly: typeof(Customer).Assembly,  // scan anchor — any domain type
            configureConventions: b => b.WithFullAudit())      // PascalCase (default)
    {
        _connectionString = connectionString;
        _auditInterceptor = new AuditInterceptor(currentUser);
    }

    /// <inheritdoc/>
    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        if (!options.IsConfigured)
            options
                .UseSqlServer(_connectionString)
                .EnableServiceProviderCaching(false)
                .AddInterceptors(_auditInterceptor);
    }
}

// -----------------------------------------------------------------------------
// No audit stamping — ICurrentUserService not needed
//
// If your application does not need audit stamping, do not call
// WithAuditFields() or WithFullAudit(). ICurrentUserService is never
// referenced and does not need to be registered or implemented.
//
// The assembly anchor (typeof(Product)) is still required — it just tells
// the builder which assembly to scan for domain entities. Pick any stable
// type from your domain assembly. It has no relationship to identity or
// audit stamping.
// -----------------------------------------------------------------------------

//public sealed class SimpleDb : UnitOfWork
//{
//    private readonly string _connectionString;
//
//    public SimpleDb(string connectionString)
//        : base(
//            domainAssembly:       typeof(Product).Assembly,  // scan anchor
//            configureConventions: b => b.UseSnakeCase())      // no WithAuditFields
//    {
//        _connectionString = connectionString;
//    }
//
//    protected override void OnConfiguring(DbContextOptionsBuilder options)
//    {
//        if (!options.IsConfigured)
//            options.UseSqlServer(_connectionString);
//        // No AddInterceptors — AuditInterceptor not needed
//    }
//}
//
// DI registration — no ICurrentUserService needed:
//
//   services.AddScoped<IUnitOfWork>(sp =>
//       new SimpleDb(connectionString));

// -----------------------------------------------------------------------------
// DI registration  [APPLICATION CODE]
// -----------------------------------------------------------------------------

/// <summary>
/// Extension methods to register the full EFConvention stack.
/// Call from <c>Program.cs</c> during application startup.
/// </summary>
public static class ServiceRegistration
{
    /// <summary>
    /// Registers all services for an ASP.NET Core web application.
    /// Uses <see cref="HttpContextCurrentUserService"/> to resolve the
    /// current user from the HTTP request.
    /// </summary>
    /// <example>
    /// <code>
    /// // Program.cs
    /// builder.Services.AddStoreServices(
    ///     builder.Configuration.GetConnectionString("StoreDb")!);
    /// </code>
    /// </example>
    public static IServiceCollection AddStoreServices(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserService, HttpContextCurrentUserService>();

        services.AddScoped<IUnitOfWork>(sp =>
            new StoreDb(
                connectionString,
                sp.GetRequiredService<ICurrentUserService>()));

        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<IProductReviewService, ProductReviewService>();

        return services;
    }

    /// <summary>
    /// Registers all services for a background job or console application.
    /// Uses <see cref="SystemUserService"/> which always returns <c>"system"</c>.
    /// </summary>
    public static IServiceCollection AddStoreServicesForBackgroundJob(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddScoped<ICurrentUserService, SystemUserService>();

        services.AddScoped<IUnitOfWork>(sp =>
            new StoreDb(
                connectionString,
                sp.GetRequiredService<ICurrentUserService>()));

        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<IProductReviewService, ProductReviewService>();

        return services;
    }
}