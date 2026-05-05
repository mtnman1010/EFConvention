using System.Reflection;
using System.Text.RegularExpressions;

namespace EFConvention
{
    /// <summary>
    /// Default convention. Preserves PascalCase names exactly as declared on the
    /// C# type. Table: <c>Customer</c>, Column: <c>OrderDate</c>, FK: <c>CustomerId</c>.
    /// </summary>
    public class PascalCaseNamingConvention : IEntityNamingConvention
    {
        /// <inheritdoc/>
        public string GetTableName(Type entityType) => entityType.Name;
        /// <inheritdoc/>
        public string GetColumnName(PropertyInfo p) => p.Name;
        /// <inheritdoc/>
        public string GetForeignKeyName(PropertyInfo nav, Type principal) => $"{nav.Name}Id";
        /// <inheritdoc/>
        public string ApplyToName(string logicalName) => logicalName;
    }

    /// <summary>
    /// Converts all PascalCase names to snake_case.
    /// Table: <c>customer</c>, Column: <c>order_date</c>, FK: <c>customer_id</c>.
    /// Recommended for PostgreSQL or any database that treats unquoted identifiers
    /// as lowercase.
    /// </summary>
    public class SnakeCaseNamingConvention : IEntityNamingConvention
    {
        /// <inheritdoc/>
        public string GetTableName(Type entityType) => ToSnakeCase(entityType.Name);
        /// <inheritdoc/>
        public string GetColumnName(PropertyInfo p) => ToSnakeCase(p.Name);
        /// <inheritdoc/>
        public string GetForeignKeyName(PropertyInfo nav, Type principal) =>
            ToSnakeCase($"{nav.Name}Id");
        /// <inheritdoc/>
        public string ApplyToName(string logicalName) => ToSnakeCase(logicalName);

        private static string ToSnakeCase(string value) =>
            Regex.Replace(value, "(?<=[a-z0-9])([A-Z])", "_$1").ToLower();
    }

    /// <summary>
    /// Pluralises table names using basic English rules while keeping column and
    /// foreign key names in PascalCase.
    /// Table: <c>Customers</c>, <c>Categories</c>. Column: <c>OrderDate</c>.
    /// FK: <c>CustomerId</c>.
    /// For advanced pluralisation (irregular nouns, non-English words) implement
    /// <see cref="IEntityNamingConvention"/> and use a library such as Humanizer.
    /// </summary>
    public class PluralizedNamingConvention : IEntityNamingConvention
    {
        /// <inheritdoc/>
        public string GetTableName(Type entityType) => Pluralize(entityType.Name);
        /// <inheritdoc/>
        public string GetColumnName(PropertyInfo p) => p.Name;
        /// <inheritdoc/>
        public string GetForeignKeyName(PropertyInfo nav, Type principal) => $"{nav.Name}Id";
        /// <inheritdoc/>
        public string ApplyToName(string logicalName) => logicalName;

        private static string Pluralize(string name)
        {
            if (name.EndsWith("y", StringComparison.OrdinalIgnoreCase))
                return name[..^1] + "ies";
            if (name.EndsWith("s", StringComparison.OrdinalIgnoreCase))
                return name + "es";
            return name + "s";
        }
    }
}
