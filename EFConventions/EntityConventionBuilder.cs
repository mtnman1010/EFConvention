// =============================================================================
// EFConventions — Version 2.1
// EntityConventionBuilder.cs
//
// Fluent facade that applies convention-over-configuration to an EF Core
// ModelBuilder. Improvements over v1 ported from LKM ConventionMapper:
//
//   [NEW] IEntityBase/IEntity interface discovery (replaces Entity base class)
//   [NEW] DeclaredOnly property scan — prevents re-configuring inherited props
//   [NEW] CanWrite guard on navigation properties
//   [NEW] Required vs optional relationship detection ([Required] + nullable FK)
//   [NEW] [Precision] attribute scan for decimal columns
//   [NEW] Private collection setter validation
//   [NEW] Virtual navigation validation when lazy loading is enabled
//   [NEW] Error collection — all problems reported together at startup
//   [NEW] ApplyToName on IEntityNamingConvention for audit/softdelete overrides
//   [IMPROVED] Configurable audit and soft-delete column names
// =============================================================================

using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace EFConvention;

/// <summary>
/// Fluent facade that applies convention-over-configuration to an EF Core
/// <see cref="ModelBuilder"/>. Discovers all types implementing
/// <see cref="IEntityBase"/> in the target assembly and automatically:
/// <list type="bullet">
///   <item><description>Registers entity tables via the active naming convention.</description></item>
///   <item><description>Maps scalar column names via the active naming convention.</description></item>
///   <item><description>Wires FK relationships from navigation property names (DeclaredOnly — no double-configuration on inheritance hierarchies).</description></item>
///   <item><description>Detects required vs optional relationships via <c>[Required]</c> and nullable FK types.</description></item>
///   <item><description>Applies <c>[Precision]</c> attributes to <c>decimal</c> columns automatically.</description></item>
///   <item><description>Validates that collection navigation properties have private or no setters.</description></item>
///   <item><description>Optionally validates that navigation properties are <c>virtual</c> when lazy loading proxies are enabled.</description></item>
///   <item><description>Optionally registers global soft-delete query filters for <see cref="ISoftDelete"/> entities.</description></item>
///   <item><description>Optionally configures audit and soft-delete column names for <see cref="IAuditable"/> and <see cref="ISoftDelete"/> entities.</description></item>
///   <item><description>Collects all configuration errors and throws them together at startup.</description></item>
/// </list>
/// </summary>
/// <example>
/// <code>
/// // In UnitOfWork.OnModelCreating:
/// EntityConventionBuilder
///     .ForAssemblyOf&lt;Customer&gt;()
///     .UseSnakeCase()
///     .WithFullAudit()
///     .Apply(modelBuilder);
/// </code>
/// </example>
public sealed class EntityConventionBuilder
{
    private readonly Assembly _domainAssembly;
    private IEntityNamingConvention _naming         = new PascalCaseNamingConvention();
    private bool _useSoftDelete                      = false;
    private bool _useAudit                           = false;
    private bool _useLazyLoading                     = false;
    private AuditColumnNames _auditColumns           = new();
    private SoftDeleteColumnNames _softDeleteColumns = new();
    private readonly List<string> _errors            = new();

    private EntityConventionBuilder(Assembly domainAssembly)
    {
        _domainAssembly = domainAssembly;
    }

    // -------------------------------------------------------------------------
    // Factory entry points
    // -------------------------------------------------------------------------

    /// <summary>Scans the specified assembly for <see cref="IEntityBase"/> types.</summary>
    public static EntityConventionBuilder ForAssembly(Assembly assembly) => new(assembly);

    /// <summary>
    /// Scans the assembly containing <typeparamref name="T"/>.
    /// Use any domain type as the anchor.
    /// </summary>
    public static EntityConventionBuilder ForAssemblyOf<T>() => new(typeof(T).Assembly);

    // -------------------------------------------------------------------------
    // Naming conventions
    // -------------------------------------------------------------------------

    /// <summary>
    /// Applies a custom naming convention replacing the default
    /// <see cref="PascalCaseNamingConvention"/>. Built-in options:
    /// <see cref="PascalCaseNamingConvention"/>,
    /// <see cref="SnakeCaseNamingConvention"/>,
    /// <see cref="PluralizedNamingConvention"/>.
    /// </summary>
    public EntityConventionBuilder UseNamingConvention(IEntityNamingConvention convention)
    {
        _naming = convention;
        return this;
    }

    /// <summary>
    /// Converts all table, column, and FK names to snake_case.
    /// <c>OrderDate</c> → <c>order_date</c>, <c>CustomerId</c> → <c>customer_id</c>.
    /// Recommended for PostgreSQL.
    /// </summary>
    public EntityConventionBuilder UseSnakeCase() =>
        UseNamingConvention(new SnakeCaseNamingConvention());

    /// <summary>
    /// Pluralises table names (<c>Customer</c> → <c>Customers</c>,
    /// <c>Category</c> → <c>Categories</c>) while keeping column and FK names
    /// in PascalCase.
    /// </summary>
    public EntityConventionBuilder UsePluralizedTables() =>
        UseNamingConvention(new PluralizedNamingConvention());

    // -------------------------------------------------------------------------
    // Optional behaviors
    // -------------------------------------------------------------------------

    /// <summary>
    /// Enables soft delete for <see cref="ISoftDelete"/> entities. Registers a
    /// global query filter (<c>WHERE is_deleted = 0</c>) automatically.
    /// Use <paramref name="configure"/> to override default column names for
    /// legacy schemas — the active naming convention is still applied on top.
    /// </summary>
    /// <example>
    /// <code>
    /// // Default names (convention applied): is_deleted, deleted_at, deleted_by
    /// .WithSoftDelete()
    ///
    /// // Legacy schema override:
    /// .WithSoftDelete(cols => { cols.IsDeleted = "Archived"; cols.DeletedAt = "ArchivedAt"; })
    /// </code>
    /// </example>
    public EntityConventionBuilder WithSoftDelete(Action<SoftDeleteColumnNames>? configure = null)
    {
        _useSoftDelete = true;
        configure?.Invoke(_softDeleteColumns);
        return this;
    }

    /// <summary>
    /// Enables audit field stamping for <see cref="IAuditable"/> entities via
    /// the <c>UnitOfWork.SaveChanges</c> override.
    /// Use <paramref name="configure"/> to override default column names for
    /// legacy schemas — the active naming convention is still applied on top.
    /// </summary>
    /// <example>
    /// <code>
    /// // Default names (convention applied): created_at, created_by, modified_at, modified_by
    /// .WithAuditFields()
    ///
    /// // Legacy schema override:
    /// .WithAuditFields(cols => { cols.CreatedAt = "RecordCreatedDate"; cols.CreatedBy = "RecordCreatedUser"; })
    /// </code>
    /// </example>
    public EntityConventionBuilder WithAuditFields(Action<AuditColumnNames>? configure = null)
    {
        _useAudit = true;
        configure?.Invoke(_auditColumns);
        return this;
    }

    /// <summary>
    /// Enables both <see cref="WithSoftDelete()"/> and <see cref="WithAuditFields()"/>
    /// in one call — the complete audit story: created, modified, and soft-deleted,
    /// with deleted rows hidden from all normal queries.
    /// </summary>
    public EntityConventionBuilder WithFullAudit() =>
        WithSoftDelete().WithAuditFields();

    /// <summary>
    /// Informs the builder that <c>UseLazyLoadingProxies()</c> is active on the
    /// DbContext options. When set, <see cref="Apply"/> validates that all
    /// navigation properties on discovered entities are declared <c>virtual</c>
    /// and reports errors for any that are not.
    /// </summary>
    public EntityConventionBuilder WithLazyLoadingValidation()
    {
        _useLazyLoading = true;
        return this;
    }

    // -------------------------------------------------------------------------
    // State — exposed for UnitOfWork.SaveChanges
    // -------------------------------------------------------------------------

    /// <summary><c>true</c> if audit stamping was enabled.</summary>
    public bool IsAuditEnabled      => _useAudit;

    /// <summary><c>true</c> if soft delete was enabled.</summary>
    public bool IsSoftDeleteEnabled => _useSoftDelete;

    // -------------------------------------------------------------------------
    // Apply
    // -------------------------------------------------------------------------

    /// <summary>
    /// Applies all configured conventions to <paramref name="modelBuilder"/>.
    /// Call once from <c>OnModelCreating</c> before <c>base.OnModelCreating</c>.
    /// Throws <see cref="InvalidOperationException"/> if any configuration
    /// errors are detected, listing all problems together.
    /// </summary>
    public void Apply(ModelBuilder modelBuilder)
    {
        _errors.Clear();
        var entityTypes = DiscoverEntityTypes();

        // Pass 1 — table names, validation, soft-delete filters
        foreach (var type in entityTypes)
        {
            modelBuilder.Entity(type).ToTable(_naming.GetTableName(type));
            ValidateCollectionSetters(type);

            if (_useLazyLoading)
                ValidateNavigationVirtuality(type, entityTypes);

            if (_useSoftDelete && typeof(ISoftDelete).IsAssignableFrom(type))
                ApplySoftDeleteFilter(modelBuilder, type);
        }

        // Pass 2 — columns, precision, relationships, audit/softdelete column names
        foreach (var type in entityTypes)
        {
            ConfigureColumns(modelBuilder, type);
            ConfigureDecimalPrecision(modelBuilder, type);
            ConfigureRelationships(modelBuilder, type, entityTypes);

            if (_useAudit && typeof(IAuditable).IsAssignableFrom(type))
                ConfigureAuditColumns(modelBuilder, type);

            if (_useSoftDelete && typeof(ISoftDelete).IsAssignableFrom(type))
                ConfigureSoftDeleteColumns(modelBuilder, type);
        }

        if (_errors.Any())
            throw new InvalidOperationException(
                $"EFConventions — {_errors.Count} configuration error(s) detected:\n" +
                string.Join("\n", _errors.Select((e, i) => $"  {i + 1}. {e}")));
    }

    // -------------------------------------------------------------------------
    // Private — discovery
    // -------------------------------------------------------------------------

    private IReadOnlyList<Type> DiscoverEntityTypes() =>
        _domainAssembly.GetTypes()
            .Where(t => t.IsClass &&
                        !t.IsAbstract &&
                        typeof(IEntityBase).IsAssignableFrom(t))
            .ToList();

    // -------------------------------------------------------------------------
    // Private — navigation property helpers (ported from LKM EntityTypeExtensions)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns writable, directly-declared reference navigation properties
    /// (properties whose type is a known entity type). Uses DeclaredOnly to
    /// prevent re-configuring inherited navigations on subclasses.
    /// </summary>
    private static IEnumerable<PropertyInfo> GetReferenceProperties(
        Type entityType, IReadOnlyList<Type> allEntityTypes) =>
        entityType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(p => p.CanWrite && allEntityTypes.Contains(p.PropertyType));

    /// <summary>
    /// Returns directly-declared collection navigation properties
    /// (ICollection/List/IList of a known entity type). Uses DeclaredOnly
    /// to prevent re-configuring inherited collections on subclasses.
    /// </summary>
    private static IEnumerable<PropertyInfo> GetCollectionProperties(
        Type entityType, IReadOnlyList<Type> allEntityTypes) =>
        entityType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(p => IsCollectionOfEntity(p.PropertyType, allEntityTypes, out _));

    // -------------------------------------------------------------------------
    // Private — startup validation
    // -------------------------------------------------------------------------

    /// <summary>
    /// Validates that collection navigation properties have private or no
    /// setters. A public setter allows callers to replace the entire collection
    /// from outside the aggregate, breaking encapsulation. All violations are
    /// accumulated and reported together at the end of <see cref="Apply"/>.
    /// </summary>
    private void ValidateCollectionSetters(Type entityType)
    {
        foreach (var prop in entityType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            var pt = prop.PropertyType;
            if (!pt.IsGenericType) continue;
            var def = pt.GetGenericTypeDefinition();
            if (def != typeof(ICollection<>) &&
                def != typeof(List<>)         &&
                def != typeof(IList<>)) continue;

            var setter = prop.GetSetMethod(nonPublic: false);
            if (setter != null)
                _errors.Add(
                    $"{entityType.Name}.{prop.Name} has a public setter. " +
                    "Collection navigation properties must use 'private set;' or no setter " +
                    "to prevent external replacement of the collection.");
        }
    }

    /// <summary>
    /// When lazy loading proxies are enabled, validates that all navigation
    /// properties on the entity are declared <c>virtual</c>. EF Core's proxy
    /// generator must override navigation getters; a non-virtual navigation
    /// silently breaks lazy loading at runtime.
    /// </summary>
    private void ValidateNavigationVirtuality(
        Type entityType, IReadOnlyList<Type> allEntityTypes)
    {
        foreach (var prop in entityType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var isNav = allEntityTypes.Contains(prop.PropertyType) ||
                        IsCollectionOfEntity(prop.PropertyType, allEntityTypes, out _);
            if (!isNav) continue;

            var getter = prop.GetGetMethod();
            if (getter != null && !getter.IsVirtual)
                _errors.Add(
                    $"{entityType.Name}.{prop.Name} must be virtual. " +
                    "Lazy loading proxies require virtual navigation properties.");
        }
    }

    // -------------------------------------------------------------------------
    // Private — soft delete
    // -------------------------------------------------------------------------

    private void ApplySoftDeleteFilter(ModelBuilder modelBuilder, Type type)
    {
        var param = Expression.Parameter(type, "e");
        var filter = Expression.Lambda(
            Expression.Equal(
                Expression.Property(param, nameof(ISoftDelete.IsDeleted)),
                Expression.Constant(false)),
            param);
        modelBuilder.Entity(type).HasQueryFilter(filter);
    }

    private void ConfigureSoftDeleteColumns(ModelBuilder modelBuilder, Type type)
    {
        var e = modelBuilder.Entity(type);
        e.Property<bool>   (nameof(ISoftDelete.IsDeleted))
         .HasColumnName(_naming.ApplyToName(_softDeleteColumns.IsDeleted));
        e.Property<DateTime?>(nameof(ISoftDelete.DeletedAt))
         .HasColumnName(_naming.ApplyToName(_softDeleteColumns.DeletedAt));
        e.Property<string?>(nameof(ISoftDelete.DeletedBy))
         .HasColumnName(_naming.ApplyToName(_softDeleteColumns.DeletedBy));
    }

    // -------------------------------------------------------------------------
    // Private — audit columns
    // -------------------------------------------------------------------------

    private void ConfigureAuditColumns(ModelBuilder modelBuilder, Type type)
    {
        var e = modelBuilder.Entity(type);
        e.Property<DateTime> (nameof(IAuditable.CreatedAt))
         .HasColumnName(_naming.ApplyToName(_auditColumns.CreatedAt));
        e.Property<string>   (nameof(IAuditable.CreatedBy))
         .HasColumnName(_naming.ApplyToName(_auditColumns.CreatedBy));
        e.Property<DateTime?>(nameof(IAuditable.ModifiedAt))
         .HasColumnName(_naming.ApplyToName(_auditColumns.ModifiedAt));
        e.Property<string?>  (nameof(IAuditable.ModifiedBy))
         .HasColumnName(_naming.ApplyToName(_auditColumns.ModifiedBy));
    }

    // -------------------------------------------------------------------------
    // Private — decimal precision
    // -------------------------------------------------------------------------

    /// <summary>
    /// Scans decimal and decimal? properties for EF Core's built-in
    /// <c>[Precision(precision, scale)]</c> attribute and applies it to the
    /// column mapping. Prevents silent decimal truncation without requiring
    /// explicit fluent configuration per property.
    /// </summary>
    private static void ConfigureDecimalPrecision(ModelBuilder modelBuilder, Type entityType)
    {
        foreach (var prop in entityType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(decimal) ||
                        p.PropertyType == typeof(decimal?)))
        {
            var attr = prop.GetCustomAttribute<Microsoft.EntityFrameworkCore.PrecisionAttribute>();
            if (attr == null) continue;

            var builder = modelBuilder.Entity(entityType)
                .Property(prop.PropertyType, prop.Name);

            if (attr.Scale.HasValue)
                builder.HasPrecision(attr.Precision, attr.Scale.Value);
            else
                builder.HasPrecision(attr.Precision);
        }
    }

    // -------------------------------------------------------------------------
    // Private — column naming
    // -------------------------------------------------------------------------

    private static readonly HashSet<Type> _scalarTypes = new()
    {
        typeof(string),  typeof(DateTime), typeof(DateTimeOffset),
        typeof(decimal), typeof(Guid),     typeof(bool),
        typeof(byte),    typeof(short),    typeof(int),
        typeof(long),    typeof(float),    typeof(double)
    };

    private void ConfigureColumns(ModelBuilder modelBuilder, Type entityType)
    {
        var props = entityType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => _scalarTypes.Contains(p.PropertyType) ||
                        (p.PropertyType.IsValueType &&
                         Nullable.GetUnderlyingType(p.PropertyType) is { } u &&
                         _scalarTypes.Contains(u)));

        foreach (var prop in props)
            modelBuilder.Entity(entityType)
                .Property(prop.PropertyType, prop.Name)
                .HasColumnName(_naming.GetColumnName(prop));
    }

    // -------------------------------------------------------------------------
    // Private — relationships
    // -------------------------------------------------------------------------

    private void ConfigureRelationships(
        ModelBuilder modelBuilder,
        Type entityType,
        IReadOnlyList<Type> allEntityTypes)
    {
        // Collections first — single-reference check skips back-references
        // that are already covered by the collection side.
        foreach (var prop in GetCollectionProperties(entityType, allEntityTypes))
            if (IsCollectionOfEntity(prop.PropertyType, allEntityTypes, out var elementType))
                ConfigureOneToMany(modelBuilder, entityType, elementType!, prop, allEntityTypes);

        foreach (var prop in GetReferenceProperties(entityType, allEntityTypes))
            ConfigureSingleReference(modelBuilder, entityType, prop.PropertyType, prop, allEntityTypes);
    }

    private void ConfigureOneToMany(
        ModelBuilder modelBuilder,
        Type principalType,
        Type dependentType,
        PropertyInfo collectionProp,
        IReadOnlyList<Type> allEntityTypes)
    {
        var inverse = GetReferenceProperties(dependentType, allEntityTypes)
            .FirstOrDefault(p => p.PropertyType == principalType);

        var fkProp  = inverse != null ? GetFkProperty(dependentType, inverse) : null;
        var fkName  = _naming.GetForeignKeyName(inverse ?? collectionProp, principalType);
        var entity  = modelBuilder.Entity(principalType);

        if (inverse != null)
            entity.HasMany(dependentType, collectionProp.Name)
                  .WithOne(inverse.Name)
                  .HasForeignKey(fkName)
                  .IsRequired(IsRequiredNavigation(inverse, fkProp));
        else
            entity.HasMany(dependentType, collectionProp.Name)
                  .WithOne()
                  .HasForeignKey(fkName);
    }

    private void ConfigureSingleReference(
        ModelBuilder modelBuilder,
        Type dependentType,
        Type principalType,
        PropertyInfo refProp,
        IReadOnlyList<Type> allEntityTypes)
    {
        // Skip — the collection side already registered this relationship.
        var principalHasCollection = GetCollectionProperties(principalType, allEntityTypes)
            .Any(p => IsCollectionPropertyOf(p.PropertyType, dependentType));
        if (principalHasCollection) return;

        var fkProp   = GetFkProperty(dependentType, refProp);
        var fkName   = _naming.GetForeignKeyName(refProp, principalType);
        var required = IsRequiredNavigation(refProp, fkProp);

        modelBuilder.Entity(dependentType)
            .HasOne(principalType, refProp.Name)
            .WithMany()
            .HasForeignKey(fkName)
            .IsRequired(required);
    }

    // -------------------------------------------------------------------------
    // Private — required/optional detection
    // -------------------------------------------------------------------------

    /// <summary>
    /// A navigation is required when either:
    /// (a) the navigation property carries <c>[Required]</c>, or
    /// (b) the corresponding FK property is a non-nullable value type (e.g. int
    ///     rather than int?), indicating the database column cannot be NULL.
    /// </summary>
    private static bool IsRequiredNavigation(PropertyInfo navProp, PropertyInfo? fkProp)
    {
        if (navProp.GetCustomAttributes<RequiredAttribute>(inherit: true).Any())
            return true;

        if (fkProp != null)
            return fkProp.PropertyType.IsValueType &&
                   Nullable.GetUnderlyingType(fkProp.PropertyType) == null;

        return false;
    }

    /// <summary>
    /// Looks for a conventionally-named FK scalar property on
    /// <paramref name="entityType"/> — e.g. for a navigation <c>Customer</c>
    /// it looks for <c>CustomerId</c>.
    /// </summary>
    private static PropertyInfo? GetFkProperty(Type entityType, PropertyInfo navProp) =>
        entityType.GetProperty(
            navProp.Name + "Id",
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

    // -------------------------------------------------------------------------
    // Private — collection helpers
    // -------------------------------------------------------------------------

    private static bool IsCollectionOfEntity(
        Type propType,
        IReadOnlyList<Type> allEntityTypes,
        out Type? elementType)
    {
        elementType = null;
        if (!propType.IsGenericType) return false;
        var def = propType.GetGenericTypeDefinition();
        if (def != typeof(ICollection<>) &&
            def != typeof(List<>)         &&
            def != typeof(IList<>)) return false;
        var arg = propType.GetGenericArguments()[0];
        if (!allEntityTypes.Contains(arg)) return false;
        elementType = arg;
        return true;
    }

    private static bool IsCollectionPropertyOf(Type propType, Type elementType)
    {
        if (!propType.IsGenericType) return false;
        return propType.GetGenericArguments().FirstOrDefault() == elementType;
    }
}
