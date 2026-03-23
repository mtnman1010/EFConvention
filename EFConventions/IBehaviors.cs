using System;
using System.Collections.Generic;
using System.Text;

namespace EFConventions
{
    // -----------------------------------------------------------------------------
    // Behavioural contracts
    //
    //   IAuditable            — automatic created/modified stamping
    //   ISoftDelete           — soft delete with global query filter
    // -----------------------------------------------------------------------------

    /// <summary>
    /// Marks an entity for automatic audit field stamping. When
    /// <see cref="EntityConventionBuilder.WithAuditFields()"/> is enabled,
    /// <c>CreatedAt</c> and <c>CreatedBy</c> are set on INSERT and
    /// <c>ModifiedAt</c> / <c>ModifiedBy</c> are stamped on every UPDATE —
    /// all via <see cref="Data.AuditInterceptor"/>. Services never set these
    /// fields directly.
    /// </summary>
    /// <remarks>
    /// Requires <see cref="ICurrentUserService"/> to be registered in DI.
    /// Falls back to <c>"system"</c> when <c>UserName</c> is null.
    /// </remarks>
    public interface IAuditable
    {
        /// <summary>UTC timestamp when this record was first created.</summary>
        DateTime CreatedAt { get; set; }

        /// <summary>Identity of the user or process that created this record.</summary>
        string CreatedBy { get; set; }

        /// <summary>UTC timestamp of the most recent modification, or <c>null</c> if never modified.</summary>
        DateTime? ModifiedAt { get; set; }

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
        DateTime? DeletedAt { get; set; }

        /// <summary>Identity of the user that deleted this record, or <c>null</c> if active.</summary>
        string? DeletedBy { get; set; }
    }
}
