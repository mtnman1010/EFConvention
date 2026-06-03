namespace EFConvention
{
    // =============================================================================
    // EFConvention — Version 2.2
    // Behaviors.cs
    //
    // Column name configuration classes for IAuditable and ISoftDelete.
    //
    // Breaking changes in v2.2:
    //   AuditColumnNames defaults changed:
    //     CreatedDate  → CreateDate
    //     CreatedBy    → CreateUser
    //     ModifiedDate → ModifyDate
    //     ModifiedBy   → ModifyUser
    //   SoftDeleteColumnNames defaults changed:
    //     DeletedDate  → DeleteDate
    //     DeletedBy    → DeleteUser
    //
    // To retain v2.1 column names, override explicitly:
    //   .WithAuditFields(cols => {
    //       cols.CreatedDate  = "CreatedAt";
    //       cols.CreatedBy    = "CreatedBy";
    //       cols.ModifiedDate = "ModifiedAt";
    //       cols.ModifiedBy   = "ModifiedBy";
    //   })
    // =============================================================================
    public interface IAuditable
    {
        /// <summary>UTC timestamp when this record was first created.</summary>
        DateTime CreatedDate { get; set; }

        /// <summary>Identity of the user or process that created this record.</summary>
        string CreatedBy { get; set; }

        /// <summary>UTC timestamp of the most recent modification, or <c>null</c> if never modified.</summary>
        DateTime? ModifiedDate { get; set; }

        /// <summary>Identity of the user or process that last modified this record.</summary>
        string? ModifiedBy { get; set; }
    }

    /// <summary>
    /// Marks an entity for soft delete. When
    /// <see cref="EntityConventionBuilder.WithSoftDelete()"/> is enabled, a
    /// global EF query filter appends <c>WHERE is_deleted = 0</c> to every query
    /// so deleted rows are invisible to normal queries. The physical row is never
    /// removed, preserving referential integrity and enabling recovery.
    /// </summary>
    /// <remarks>
    /// To query deleted rows call
    /// <c>unitOfWork.Query&lt;T&gt;().IgnoreQueryFilters()</c>.
    /// Use <see cref="EntityConventionBuilder.WithFullAudit"/> to also capture
    /// who deleted the record via <c>DeletedBy</c>.
    /// </remarks>
    public interface ISoftDelete
    {
        /// <summary><c>true</c> if this record has been soft-deleted.</summary>
        bool IsDeleted { get; set; }

        /// <summary>UTC timestamp when soft-deleted, or <c>null</c> if active.</summary>
        DateTime? DeletedDate { get; set; }

        /// <summary>Identity of the user that deleted this record, or <c>null</c> if active.</summary>
        string? DeletedBy { get; set; }
    }
}
