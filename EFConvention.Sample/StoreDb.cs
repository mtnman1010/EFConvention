// =============================================================================
// EFConvention — Version 2.2
// Data/StoreDb.cs  [APPLICATION CODE — not part of the library]
//
// Changes from v2.1:
//   Convention updated from UseSnakeCase() to default PascalCase to match
//   the StoreDb.sql schema script. Snake_case is still fully supported —
//   see the README for the UseSnakeCase() configuration pattern.
//
// Contents:
//   HttpContextCurrentUserService — ASP.NET Core web app implementation
//   SystemUserService             — background job / console app implementation
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
// -----------------------------------------------------------------------------

/// <summary>
/// ASP.NET Core implementation. Resolves the username from the current HTTP
/// request's claims principal via <see cref="IHttpContextAccessor"/>.
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
/// <c>b => b.UseSnakeCase().WithFullAudit()</c> and update your schema script
/// accordingly.
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
            domainAssembly: typeof(Customer).Assembly,
            configureConventions: b => b.WithFullAudit())  // PascalCase (default)
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