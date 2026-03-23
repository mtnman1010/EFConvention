namespace EFConventions
{
    // -----------------------------------------------------------------------------
    // Column name configuration overrides
    //
    //   AuditColumnNames      — configurable column name overrides for IAuditable
    //   SoftDeleteColumnNames — configurable column name overrides for ISoftDelete
    // -----------------------------------------------------------------------------

    /// <summary>
    /// Logical property names used when mapping <see cref="IAuditable"/> columns.
    /// The active <see cref="IEntityNamingConvention"/> is applied on top —
    /// <c>CreatedAt</c> becomes <c>created_at</c> under snake_case.
    /// </summary>
    public sealed class AuditColumnNames
    {
        /// <summary>Default: <c>CreatedAt</c></summary>
        public string CreatedAt { get; set; } = "CreatedAt";
        /// <summary>Default: <c>CreatedBy</c></summary>
        public string CreatedBy { get; set; } = "CreatedBy";
        /// <summary>Default: <c>ModifiedAt</c></summary>
        public string ModifiedAt { get; set; } = "ModifiedAt";
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
        /// <summary>Default: <c>DeletedAt</c></summary>
        public string DeletedAt { get; set; } = "DeletedAt";
        /// <summary>Default: <c>DeletedBy</c></summary>
        public string DeletedBy { get; set; } = "DeletedBy";
    }
}
