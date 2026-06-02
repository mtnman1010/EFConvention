// =============================================================================
// EFConvention — Version 2.2
// EntityConventionBuilder.cs
//
// Fluent facade that applies convention-over-configuration to an EF Core
// ModelBuilder.
//
// Changes in v2.2:
//   [BREAKING] FK columns named after navigation property, not type+Id
//              e.g. Withholding.Person → FK column "Person" (was "PersonId")
//   [BREAKING] Audit column defaults changed:
//              CreatedAt → CreateDate, ModifiedAt → ModifyDate
//              CreatedBy → CreateUser, ModifiedBy → ModifyUser
//   [NEW]      GetCollection disambiguation — multiple collections of the same
//              type resolved by StartsWith convention matching, e.g.
//              PersonWithholdings → Person nav, PayeeWithholdings → Payee nav
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
///   <item><description>Wires FK relationships using the navigation property name as the FK column name.</description></item>
///   <item><description>Resolves multiple collections of the same type using StartsWith name convention.</description></item>
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
    private IEntityNamingConvention _naming = new PascalCaseNamingConvention();
    private bool _useSoftDelete = false;
    private bool _useAudit = false;
    private bool _useLazyLoading = false;
    private AuditColumnNames _auditColumns = new();
    private SoftDeleteColumnNames _softDeleteColumns = new();
    private readonly List<string> _errors = new();

    private readonly IReadOnlyList<Type>? _explicitTypes;

    private EntityConventionBuilder(Assembly domainAssembly)
    {
        _domainAssembly = domainAssembly;
    }

    private EntityConventionBuilder(IReadOnlyList<Type> explicitTypes)
    {
        _domainAssembly = null!;
        _explicitTypes = explicitTypes;
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

    /// <summary>
    /// Registers only the specified types rather than scanning an entire assembly.
    /// Useful for testing or when you want to register a subset of entities.
    /// </summary>
    public static EntityConventionBuilder ForTypes(params Type[] types) => new(types);

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
    /// Converts all table and column names to snake_case.
    /// <c>OrderDate</c> → <c>order_date</c>, FK <c>Person</c> → <c>person</c>.
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
    /// global query filter (<c>WHERE IsDeleted = 0</c>) automatically.
    /// Use <paramref name="configure"/> to override default column names for
    /// legacy schemas — the active naming convention is still applied on top.
    /// </summary>
    public EntityConventionBuilder WithSoftDelete(Action<SoftDeleteColumnNames>? configure = null)
    {
        _useSoftDelete = true;
        configure?.Invoke(_softDeleteColumns);
        return this;
    }

    /// <summary>
    /// Enables audit field stamping for <see cref="IAuditable"/> entities.
    /// Use <paramref name="configure"/> to override default column names for
    /// legacy schemas — the active naming convention is still applied on top.
    /// </summary>
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
    /// navigation properties are declared <c>virtual</c>.
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
    public bool IsAuditEnabled => _useAudit;

    /// <summary><c>true</c> if soft delete was enabled.</summary>
    public bool IsSoftDeleteEnabled => _useSoftDelete;

    // -------------------------------------------------------------------------
    // Apply
    // -------------------------------------------------------------------------

    /// <summary>
    /// Applies all configured conventions to <paramref name="modelBuilder"/>.
    /// Call once from <c>OnModelCreating</c> after <c>base.OnModelCreating</c>.
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
                $"EFConvention — {_errors.Count} configuration error(s) detected:\n" +
                string.Join("\n", _errors.Select((e, i) => $"  {i + 1}. {e}")));
    }

    // -------------------------------------------------------------------------
    // Private — discovery
    // -------------------------------------------------------------------------

    private IReadOnlyList<Type> DiscoverEntityTypes() =>
        _explicitTypes?.ToList() ??
        _domainAssembly.GetTypes()
            .Where(t => t.IsClass &&
                        !t.IsAbstract &&
                        typeof(IEntityBase).IsAssignableFrom(t))
            .ToList();

    // -------------------------------------------------------------------------
    // Private — navigation property helpers
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

    /// <summary>
    /// Finds the inverse collection on the principal for a given reference
    /// navigation on the dependent. Handles multiple collections of the same
    /// type by matching the collection name to the navigation property name
    /// using a StartsWith convention.
    ///
    /// <para>
    /// Example — <c>Withholding</c> has three navigations to <c>Person</c>:
    /// <c>Person</c>, <c>Payee</c>, <c>OnBehalfOf</c>. On <c>Person</c>:
    /// <list type="bullet">
    ///   <item><description><c>PersonWithholdings.StartsWith("Person")</c> → maps to <c>Person</c> nav</description></item>
    ///   <item><description><c>PayeeWithholdings.StartsWith("Payee")</c> → maps to <c>Payee</c> nav</description></item>
    ///   <item><description><c>OnBehalfOfWithholdings.StartsWith("OnBehalfOf")</c> → maps to <c>OnBehalfOf</c> nav</description></item>
    /// </list>
    /// </para>
    /// </summary>
    private static PropertyInfo? GetCollection(
        PropertyInfo navProp,
        IReadOnlyList<Type> allEntityTypes)
    {
        // Find all collections on the principal type whose element type
        // matches the declaring type of the navigation property
        var matchingCollections = navProp.PropertyType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite &&
                        IsCollectionOfEntity(p.PropertyType, allEntityTypes, out var el) &&
                        el == navProp.DeclaringType)
            .ToList();

        if (!matchingCollections.Any())
            return null;

        // Single match — unambiguous
        if (matchingCollections.Count == 1)
            return matchingCollections.Single();

        // Multiple collections of same type — disambiguate by name convention:
        // the collection whose name starts with the navigation property name
        // e.g. "PersonWithholdings".StartsWith("Person") → Person navigation
        return matchingCollections
            .FirstOrDefault(p => p.Name.StartsWith(navProp.Name));
    }

    // -------------------------------------------------------------------------
    // Private — startup validation
    // -------------------------------------------------------------------------

    private void ValidateCollectionSetters(Type entityType)
    {
        foreach (var prop in entityType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            var pt = prop.PropertyType;
            if (!pt.IsGenericType) continue;
            var def = pt.GetGenericTypeDefinition();
            if (def != typeof(ICollection<>) &&
                def != typeof(List<>) &&
                def != typeof(IList<>)) continue;

            var setter = prop.GetSetMethod(nonPublic: false);
            if (setter != null)
                _errors.Add(
                    $"{entityType.Name}.{prop.Name} has a public setter. " +
                    "Collection navigation properties must use 'private set;' or no setter " +
                    "to prevent external replacement of the collection.");
        }
    }

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
        e.Property<bool>(nameof(ISoftDelete.IsDeleted))
         .HasColumnName(_naming.ApplyToName(_softDeleteColumns.IsDeleted));
        e.Property<DateTime?>(nameof(ISoftDelete.DeletedDate))
         .HasColumnName(_naming.ApplyToName(_softDeleteColumns.DeletedDate));
        e.Property<string?>(nameof(ISoftDelete.DeletedBy))
         .HasColumnName(_naming.ApplyToName(_softDeleteColumns.DeletedBy));
    }

    // -------------------------------------------------------------------------
    // Private — audit columns
    // -------------------------------------------------------------------------

    private void ConfigureAuditColumns(ModelBuilder modelBuilder, Type type)
    {
        var e = modelBuilder.Entity(type);
        e.Property<DateTime>(nameof(IAuditable.CreatedDate))
         .HasColumnName(_naming.ApplyToName(_auditColumns.CreatedDate));
        e.Property<string>(nameof(IAuditable.CreatedBy))
         .HasColumnName(_naming.ApplyToName(_auditColumns.CreatedBy));
        e.Property<DateTime?>(nameof(IAuditable.ModifiedDate))
         .HasColumnName(_naming.ApplyToName(_auditColumns.ModifiedDate));
        e.Property<string?>(nameof(IAuditable.ModifiedBy))
         .HasColumnName(_naming.ApplyToName(_auditColumns.ModifiedBy));
    }

    // -------------------------------------------------------------------------
    // Private — decimal precision
    // -------------------------------------------------------------------------

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
        // Reference navigations on the dependent side drive relationship config.
        // Collections on the principal are found via GetCollection() and used
        // to wire the WithMany(collectionName) side of the relationship.
        foreach (var refProp in GetReferenceProperties(entityType, allEntityTypes))
            ConfigureRelationship(modelBuilder, entityType, refProp, allEntityTypes);
    }

    /// <summary>
    /// Configures a single reference navigation property as an EF relationship.
    ///
    /// <para>
    /// FK column is named after the <b>navigation property name</b> — not the
    /// principal type name with "Id" suffix. This matches the ASRC convention:
    /// <list type="bullet">
    ///   <item><description><c>Withholding.Person</c> → FK column <c>Person</c></description></item>
    ///   <item><description><c>Withholding.Payee</c> → FK column <c>Payee</c></description></item>
    ///   <item><description><c>Withholding.OnBehalfOf</c> → FK column <c>OnBehalfOf</c></description></item>
    /// </list>
    /// </para>
    /// </summary>
    private void ConfigureRelationship(
        ModelBuilder modelBuilder,
        Type dependentType,
        PropertyInfo refProp,
        IReadOnlyList<Type> allEntityTypes)
    {
        var principalType = refProp.PropertyType;

        // Find the inverse collection on the principal using StartsWith convention
        var collection = GetCollection(refProp, allEntityTypes);

        // Required/optional detection — [Required] attribute or non-nullable FK scalar
        var fkScalar = GetFkProperty(dependentType, refProp);
        var required = IsRequiredNavigation(refProp, fkScalar);

        var dependent = modelBuilder.Entity(dependentType);

        if (collection != null)
        {
            var setter = collection.GetSetMethod(nonPublic: false);
            if (setter != null)
                _errors.Add(
                    $"{collection.DeclaringType!.Name}.{collection.Name} has a public setter. " +
                    "Collection navigation properties must use 'private set;' or no setter.");

            dependent
                .HasOne(principalType, refProp.Name)
                .WithMany(collection.Name)
                .IsRequired(required);
        }
        else
        {
            dependent
                .HasOne(principalType, refProp.Name)
                .WithMany()
                .IsRequired(required);
        }

        // After wiring, rename the FK column to the navigation property name.
        // EF Core auto-creates a shadow property named "{NavName}Id" — we keep
        // that internal name but rename the database column to just "{NavName}".
        var shadowPropName = fkScalar?.Name ?? (refProp.Name + "Id");
        try
        {
            modelBuilder.Entity(dependentType)
                .Property(shadowPropName)
                .HasColumnName(_naming.ApplyToName(refProp.Name));
        }
        catch
        {
            // If the shadow property doesn't exist or can't be renamed, leave it
            // as EF Core named it — don't break the build over column naming
        }
    }

    // -------------------------------------------------------------------------
    // Private — required/optional detection
    // -------------------------------------------------------------------------

    /// <summary>
    /// A navigation is required when either:
    /// (a) the navigation property carries <c>[Required]</c>, or
    /// (b) the corresponding FK scalar property is a non-nullable value type.
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
    /// Looks for a conventionally-named FK scalar property on the dependent —
    /// e.g. for a navigation <c>Person</c> it looks for <c>PersonId</c>.
    /// Used only for required/optional detection, not for FK column naming.
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
            def != typeof(List<>) &&
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