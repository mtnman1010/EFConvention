// =============================================================================
// EFConventions — Version 2.1
// IEntityNamingConvention.cs
//
// Defines how C# type and property names are translated into database
// identifiers. Three strategies ship out of the box:
//   PascalCaseNamingConvention  — names used as-is (default)
//   SnakeCaseNamingConvention   — converts to snake_case
//   PluralizedNamingConvention  — pluralises table names, PascalCase columns
//
// Implement IEntityNamingConvention to provide a fully custom strategy.
// =============================================================================

using System.Reflection;

namespace EFConvention;

/// <summary>
/// Defines the naming strategy used by <see cref="EntityConventionBuilder"/>
/// when mapping entity types, properties, and foreign keys to database
/// identifiers. Implement this interface to supply a fully custom strategy.
/// </summary>
public interface IEntityNamingConvention
{
    /// <summary>Returns the database table name for the given entity type.</summary>
    string GetTableName(Type entityType);

    /// <summary>Returns the database column name for the given property.</summary>
    string GetColumnName(PropertyInfo property);

    /// <summary>
    /// Returns the foreign key column name for a navigation property pointing
    /// at <paramref name="principalType"/>.
    /// </summary>
    string GetForeignKeyName(PropertyInfo navigationProperty, Type principalType);

    /// <summary>
    /// Converts an arbitrary logical name (e.g. an audit column override) into
    /// the database identifier format used by this convention. Used when callers
    /// supply custom column names via <see cref="AuditColumnNames"/> or
    /// <see cref="SoftDeleteColumnNames"/>.
    /// </summary>
    string ApplyToName(string logicalName);
}

