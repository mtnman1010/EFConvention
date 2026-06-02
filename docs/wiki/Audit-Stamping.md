# Audit Stamping

EFConventionBuilder provides automatic audit field stamping via `AuditInterceptor` — an EF Core `SaveChangesInterceptor` that stamps `CreatedDate`, `CreatedBy`, `ModifiedDate`, and `ModifiedBy` on every save without any code in your services or domain objects.

---

## How it works

`AuditInterceptor` hooks into EF Core's save pipeline and stamps `IAuditable` fields before every `SaveChanges` or `SaveChangesAsync` call:

```
EntityState.Added    → CreatedDate = UtcNow,  CreatedBy = UserName
EntityState.Modified → ModifiedDate = UtcNow, ModifiedBy = UserName
```

- `CreatedDate` and `CreatedBy` are set on INSERT and **never updated again**
- `ModifiedDate` and `ModifiedBy` are `null` until the first UPDATE, then stamped on every subsequent save
- Falls back to `"system"` when `ICurrentUserService.UserName` returns null
- Fires on both `SavingChanges` and `SavingChangesAsync` — all save paths are covered

---

## The IAuditable interface

Implement `IAuditable` on any entity that needs audit stamping:

```csharp
public interface IAuditable
{
    DateTime  CreatedDate  { get; set; }
    string    CreatedBy    { get; set; }
    DateTime? ModifiedDate { get; set; }
    string?   ModifiedBy   { get; set; }
}
```

`CreatedDate` and `CreatedBy` are non-nullable — always set on INSERT.
`ModifiedDate` and `ModifiedBy` are nullable — null until the first UPDATE.

### Domain entity example

```csharp
public class Customer : IEntity, IAuditable
{
    public int    Id    { get; set; }
    public string Name  { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    // Never set these manually — AuditInterceptor handles them
    public DateTime  CreatedDate  { get; set; }
    public string    CreatedBy    { get; set; } = string.Empty;
    public DateTime? ModifiedDate { get; set; }
    public string?   ModifiedBy   { get; set; }
}
```

Never set audit properties manually in your services. `AuditInterceptor` stamps them on every save — manual assignments will be overwritten.

---

## ICurrentUserService

`ICurrentUserService` is the identity abstraction — implement it once per application to tell the interceptor who the current user is:

```csharp
public interface ICurrentUserService
{
    string? UserName { get; }
}
```

The interface is defined in the library. You provide the implementation suited to your hosting environment.

---

## Implementations by hosting environment

### ASP.NET Core MVC / Razor Pages

Resolves from the authenticated HTTP request:

```csharp
public class HttpContextCurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _accessor;
    public HttpContextCurrentUserService(IHttpContextAccessor accessor)
        => _accessor = accessor;
    public string? UserName => _accessor.HttpContext?.User?.Identity?.Name;
}

// Program.cs
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, HttpContextCurrentUserService>();
```

### ASP.NET Core Web API with JWT

Resolves from JWT claims:

```csharp
public class JwtCurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _accessor;
    public JwtCurrentUserService(IHttpContextAccessor accessor)
        => _accessor = accessor;
    public string? UserName =>
        _accessor.HttpContext?.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? _accessor.HttpContext?.User?.FindFirst("sub")?.Value;
}

// Program.cs
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, JwtCurrentUserService>();
```

### Blazor Server

Resolves from `AuthenticationStateProvider` across the SignalR circuit:

```csharp
public class BlazorCurrentUserService : ICurrentUserService
{
    private readonly AuthenticationStateProvider _authState;
    public BlazorCurrentUserService(AuthenticationStateProvider authState)
        => _authState = authState;

    public string? UserName
    {
        get
        {
            var state = _authState.GetAuthenticationStateAsync()
                .GetAwaiter().GetResult();
            return state.User?.Identity?.Name;
        }
    }
}

// Program.cs
builder.Services.AddScoped<ICurrentUserService, BlazorCurrentUserService>();
```

### WPF / WinForms — Windows authentication

Resolves from the current Windows identity (domain-joined apps):

```csharp
public class WindowsCurrentUserService : ICurrentUserService
{
    public string? UserName =>
        System.Security.Principal.WindowsIdentity.GetCurrent().Name;
}
```

### WPF / WinForms / MVVM — custom login session

Resolves from your application's session or login state:

```csharp
public class SessionCurrentUserService : ICurrentUserService
{
    private readonly ISessionService _session;
    public SessionCurrentUserService(ISessionService session)
        => _session = session;
    public string? UserName => _session.CurrentUser?.Username;
}
```

### Background jobs and hosted services

Always returns `"system"` — suitable for any process without a human user context:

```csharp
public class SystemUserService : ICurrentUserService
{
    public string? UserName => "system";
}

// Program.cs
services.AddScoped<ICurrentUserService, SystemUserService>();
```

### Console apps and CLI tools

Resolves from command line arguments or the OS user:

```csharp
public class ConsoleCurrentUserService : ICurrentUserService
{
    public string? UserName { get; }
    public ConsoleCurrentUserService(string userName) => UserName = userName;
}

// Program.cs
var userName = args.FirstOrDefault() ?? Environment.UserName;
services.AddSingleton<ICurrentUserService>(new ConsoleCurrentUserService(userName));
```

### Unit tests

Fixed identity for deterministic audit field assertions:

```csharp
public class FixedUserService : ICurrentUserService
{
    public string? UserName { get; }
    public FixedUserService(string name = "test-user") => UserName = name;
}

// Test setup
var userService = new FixedUserService("test-user");
var db = new InMemoryStoreDb(userService);
```

---

## Wiring AuditInterceptor

`AuditInterceptor` is registered via `AddInterceptors()` in your concrete `UnitOfWork` subclass. The `UnitOfWork` base class itself has no dependency on `ICurrentUserService` — the connection is made entirely in your application code:

```csharp
public sealed class StoreDb : UnitOfWork
{
    private readonly string _connectionString;
    private readonly AuditInterceptor _auditInterceptor;

    public StoreDb(string connectionString, ICurrentUserService currentUser)
        : base(
            domainAssembly:       typeof(Customer).Assembly,
            configureConventions: b => b.WithFullAudit())
    {
        _connectionString = connectionString;
        _auditInterceptor  = new AuditInterceptor(currentUser);  // wired here
    }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        if (!options.IsConfigured)
            options
                .UseSqlServer(_connectionString)
                .AddInterceptors(_auditInterceptor);  // registered here
    }
}
```

**The `UnitOfWork` base class never sees `ICurrentUserService`.** This separation means:
- The library has no dependency on your identity infrastructure
- You can swap identity implementations without touching the library
- Unit tests can inject a `FixedUserService` without any special setup

---

## Enabling audit stamping

Audit stamping requires two things — both must be present:

1. The entity implements `IAuditable`
2. The builder is configured with `.WithAuditFields()` or `.WithFullAudit()`

```csharp
// Audit only
configureConventions: b => b.WithAuditFields()

// Audit + soft delete together
configureConventions: b => b.WithFullAudit()
```

If the entity implements `IAuditable` but the builder is not configured with `WithAuditFields()`, the properties exist on the domain object but are never stamped and the columns are not configured. If the builder has `WithAuditFields()` but the entity does not implement `IAuditable`, nothing happens for that entity.

### Per-entity opt-in

| Entity implements | Builder configured with | Audit stamped? |
|---|---|---|
| `IAuditable` | `.WithAuditFields()` or `.WithFullAudit()` | ✅ Yes |
| `IAuditable` | no audit flag | ❌ No |
| neither | anything | ❌ No |

---

## Overriding default column names

Default audit column names match the C# property names exactly under PascalCase:

```
CreatedDate, CreatedBy, ModifiedDate, ModifiedBy
```

Override them for legacy schemas. The active naming convention is applied on top of the override — so under snake_case, `"RecordCreatedDate"` becomes `"record_created_date"`:

```csharp
.WithAuditFields(cols =>
{
    cols.CreatedDate  = "RecordCreatedDate";
    cols.CreatedBy    = "RecordCreatedUser";
    cols.ModifiedDate = "RecordModifiedDate";
    cols.ModifiedBy   = "RecordModifiedUser";
})
```

---

## Audit stamping and soft delete together

`WithFullAudit()` enables both audit stamping and soft delete in one call. When an entity implements both `IAuditable` and `ISoftDelete`, the soft delete fields (`DeletedDate`, `DeletedBy`) are stamped by `ServiceBase.DeleteAsync` — not by `AuditInterceptor`. The interceptor only handles `CreatedDate`, `CreatedBy`, `ModifiedDate`, and `ModifiedBy`.

```csharp
// Full audit entity
public class Order : IEntity, IAuditable, ISoftDelete
{
    public int    Id    { get; set; }
    // ...

    // Stamped by AuditInterceptor
    public DateTime  CreatedDate  { get; set; }
    public string    CreatedBy    { get; set; } = string.Empty;
    public DateTime? ModifiedDate { get; set; }
    public string?   ModifiedBy   { get; set; }

    // Stamped by ServiceBase.DeleteAsync
    public bool      IsDeleted   { get; set; }
    public DateTime? DeletedDate { get; set; }
    public string?   DeletedBy   { get; set; }
}
```

See [[Soft Delete]] for the full delete/restore/purge lifecycle.

---

## No audit stamping needed

If your application does not need audit stamping, skip `IAuditable`, skip `WithAuditFields()`, and skip `ICurrentUserService` entirely:

```csharp
public sealed class SimpleDb : UnitOfWork
{
    public SimpleDb(string connectionString)
        : base(
            domainAssembly:       typeof(Product).Assembly,
            configureConventions: b => b.UseSnakeCase())  // no WithAuditFields
    {
        _connectionString = connectionString;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        if (!options.IsConfigured)
            options.UseSqlServer(_connectionString);
        // No AddInterceptors — AuditInterceptor not needed
    }
}

// DI — no ICurrentUserService registration needed
services.AddScoped<IUnitOfWork>(sp => new SimpleDb(connectionString));
```

---

## Next steps

- [[Soft Delete]] — `ISoftDelete`, global query filter, and the delete/restore/purge lifecycle
- [[Service Base]] — how `DeleteAsync` stamps soft delete fields
- [[Testing]] — using `FixedUserService` in unit and integration tests
