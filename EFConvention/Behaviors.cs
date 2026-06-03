// =============================================================================
// EFConvention — Version 2.2
// Behaviors.cs
//
// Column name configuration classes for IAuditable and ISoftDelete.
//
// Breaking changes in v2.2:
//   AuditColumnNames defaults changed:
//     CreatedAt  → CreateDate
//     CreatedBy  → CreateUser
//     ModifiedAt → ModifyDate
//     ModifiedBy → ModifyUser
//   SoftDeleteColumnNames defaults changed:
//     DeletedAt  → DeleteDate
//     DeletedBy  → DeleteUser
//
// To retain v2.1 column names, override explicitly:
//   .WithAuditFields(cols => {
//       cols.CreatedAt  = "CreatedAt";
//       cols.CreatedBy  = "CreatedBy";
//       cols.ModifiedAt = "ModifiedAt";
//       cols.ModifiedBy = "ModifiedBy";
//   })
// =============================================================================

namespace EFConvention;

/// <summary>
/// Logical property names used when mapping <see cref="IAuditable"/> columns.
/// The active <see cref="IEntityNamingConvention"/> is applied on top —
/// <c>CreatedDate</c> becomes <c>created_date</c> under snake_case.
/// </summary>
public sealed class AuditColumnNames
{
    /// <summary>Default: <c>CreatedDate</c></summary>
    public string CreatedDate { get; set; } = "CreatedDate";
    /// <summary>Default: <c>CreatedBy</c></summary>
    public string CreatedBy { get; set; } = "CreatedBy";
    /// <summary>Default: <c>ModifiedDate</c></summary>
    public string ModifiedDate { get; set; } = "ModifiedDate";
    /// <summary>Default: <c>ModifiedBy</c></summary>
    public string ModifiedBy { get; set; } = "ModifiedBy";
}

/// <summary>
/// Logical property names used when mapping <see cref="ISoftDelete"/> columns.
/// The active naming convention is applied on top — <c>IsDeleted</c> becomes
/// <c>is_deleted</c> under snake_case.
/// </summary>
public sealed class SoftDeleteColumnNames
{
    /// <summary>Default: <c>IsDeleted</c></summary>
    public string IsDeleted { get; set; } = "IsDeleted";
    /// <summary>Default: <c>DeletedDate</c></summary>
    public string DeletedDate { get; set; } = "DeletedDate";
    /// <summary>Default: <c>DeletedBy</c></summary>
    public string DeletedBy { get; set; } = "DeletedBy";
}