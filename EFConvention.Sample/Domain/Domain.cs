// =============================================================================
// EFConvention — Version 2.2
// Domain/Domain.cs
//
// Expanded domain model for the StoreDb example. Covers every feature of
// EntityConventionBuilder:
//
//   Address       — plain entity, no audit, no soft delete
//   Category      — plain entity (pluralisation example: Categories table)
//   Customer      — IAuditable only, hard delete
//   Product       — IAuditable + ISoftDelete, decimal with [Precision]
//   Order         — IAuditable + ISoftDelete, required FK, private collection
//   OrderItem     — plain entity, required FKs to both Order and Product
//   ProductReview — optional FK (nullable CustomerId), IAuditable
//
// Navigation property conventions enforced by EntityConventionBuilder:
//   - Collection properties use 'private set;'  (public setter = startup error)
//   - Scalar FK properties are non-nullable int  → required relationship
//   - Scalar FK properties are nullable int?     → optional relationship
//   - [Required] on a navigation also forces required
//   - [Precision(18,2)] on decimal → applied automatically
//
// FK column naming (v2.2):
//   FK columns are named after the navigation property, not the scalar FK
//   property. AddressId scalar → "Address" database column,
//   CustomerId scalar → "Customer" database column, etc.
//   The scalar FK properties (AddressId, CustomerId etc.) still exist on
//   the C# entity for required/optional detection — only the DB column
//   name changes.
// =============================================================================

using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace EFConvention.Domain;

// -----------------------------------------------------------------------------
// Address — plain entity, no audit, no soft delete
// -----------------------------------------------------------------------------

/// <summary>
/// A physical mailing address. Used by <see cref="Customer"/> as a required
/// reference navigation. Plain entity — no audit fields, no soft delete.
/// </summary>
public class Address : IEntity
{
    public int Id { get; set; }
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
}

// -----------------------------------------------------------------------------
// Category — plain entity
// -----------------------------------------------------------------------------

/// <summary>
/// Product category. Demonstrates that the pluralised convention correctly
/// handles the 'y' → 'ies' rule: <c>Category</c> → <c>Categories</c>.
/// Plain entity — no audit, no soft delete.
/// </summary>
public class Category : IEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    // Private setter — collection navigation, encapsulated
    public ICollection<Product> Products { get; private set; } = new List<Product>();
}

// -----------------------------------------------------------------------------
// Customer — IAuditable only, hard delete
// -----------------------------------------------------------------------------

/// <summary>
/// A store customer. Implements <see cref="IAuditable"/> — created/modified
/// fields are stamped automatically by <see cref="AuditInterceptor"/>.
/// Does NOT implement <see cref="ISoftDelete"/> so
/// <c>CustomerService.DeleteAsync</c> issues a physical DELETE.
///
/// <para>
/// <c>AddressId</c> is <c>int</c> (non-nullable) → required relationship.
/// The convention builder calls <c>.IsRequired(true)</c> automatically.
/// Database FK column is named <c>Address</c> (navigation property name).
/// </para>
/// </summary>
public class Customer : IEntity, IAuditable
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;

    // Required reference — non-nullable int → required FK
    // DB column: "Address" (navigation name, not "AddressId")
    public int AddressId { get; set; }
    public Address Address { get; set; } = null!;

    // Private setters — encapsulated collections
    public ICollection<Order> Orders { get; private set; } = new List<Order>();
    public ICollection<ProductReview> Reviews { get; private set; } = new List<ProductReview>();

    // IAuditable — stamped automatically by AuditInterceptor
    public DateTime CreatedDate { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime? ModifiedDate { get; set; }
    public string? ModifiedBy { get; set; }
}

// -----------------------------------------------------------------------------
// Product — IAuditable + ISoftDelete + [Precision] on decimal
// -----------------------------------------------------------------------------

/// <summary>
/// A store product. Demonstrates:
/// <list type="bullet">
///   <item><description>
///     <c>[Precision(18, 2)]</c> on <c>Price</c> and <c>CostPrice</c> —
///     applied automatically by the convention builder's decimal precision pass.
///   </description></item>
///   <item><description>
///     <see cref="ISoftDelete"/> — products are never physically deleted,
///     preserving historical order data.
///   </description></item>
///   <item><description>
///     <see cref="IAuditable"/> — full audit trail.
///   </description></item>
///   <item><description>
///     <c>CategoryId</c> is <c>int</c> (non-nullable) → required FK.
///     DB column named <c>Category</c> (navigation property name).
///   </description></item>
/// </list>
/// </summary>
public class Product : IEntity, IAuditable, ISoftDelete
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;

    // [Precision] picked up automatically by ConfigureDecimalPrecision
    [Precision(18, 2)]
    public decimal Price { get; set; }

    [Precision(18, 2)]
    public decimal CostPrice { get; set; }

    // Required FK — non-nullable int
    // DB column: "Category" (navigation name, not "CategoryId")
    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;

    // Private setters — encapsulated collections
    public ICollection<OrderItem> OrderItems { get; private set; } = new List<OrderItem>();
    public ICollection<ProductReview> Reviews { get; private set; } = new List<ProductReview>();

    // IAuditable — stamped automatically by AuditInterceptor
    public DateTime CreatedDate { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime? ModifiedDate { get; set; }
    public string? ModifiedBy { get; set; }

    // ISoftDelete — managed by ServiceBase.DeleteAsync
    public bool IsDeleted { get; set; }
    public DateTime? DeletedDate { get; set; }
    public string? DeletedBy { get; set; }
}

// -----------------------------------------------------------------------------
// Order — IAuditable + ISoftDelete, required FK, private collection
// -----------------------------------------------------------------------------

/// <summary>
/// A customer order. Demonstrates:
/// <list type="bullet">
///   <item><description>
///     Both <see cref="IAuditable"/> and <see cref="ISoftDelete"/> — the full
///     audit story: who created, modified, and deleted.
///   </description></item>
///   <item><description>
///     <c>[Required]</c> on the <c>Customer</c> navigation — explicitly marks
///     the relationship as required regardless of FK nullability.
///     DB column named <c>Customer</c> (navigation property name).
///   </description></item>
///   <item><description>
///     <c>[Precision(18, 2)]</c> on <c>TotalAmount</c>.
///   </description></item>
///   <item><description>
///     Private collection setter on <c>Items</c> — validated at startup.
///   </description></item>
/// </list>
/// </summary>
public class Order : IEntity, IAuditable, ISoftDelete
{
    public int Id { get; set; }
    public DateTime OrderDate { get; set; }
    public string Status { get; set; } = "Pending";

    [Precision(18, 2)]
    public decimal TotalAmount { get; set; }

    // [Required] on navigation — explicitly required regardless of FK type
    // DB column: "Customer" (navigation name, not "CustomerId")
    [Required]
    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    // Private setter — encapsulated collection
    public ICollection<OrderItem> Items { get; private set; } = new List<OrderItem>();

    // IAuditable — stamped automatically by AuditInterceptor
    public DateTime CreatedDate { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime? ModifiedDate { get; set; }
    public string? ModifiedBy { get; set; }

    // ISoftDelete — managed by ServiceBase.DeleteAsync
    public bool IsDeleted { get; set; }
    public DateTime? DeletedDate { get; set; }
    public string? DeletedBy { get; set; }
}

// -----------------------------------------------------------------------------
// OrderItem — plain entity, required FKs to Order and Product
// -----------------------------------------------------------------------------

/// <summary>
/// A line item within an <see cref="Order"/>. Both FK properties are
/// non-nullable <c>int</c> → both relationships detected as required
/// automatically. Plain entity — no audit, no soft delete.
/// DB FK columns named <c>Order</c> and <c>Product</c>
/// (navigation property names).
/// </summary>
public class OrderItem : IEntity
{
    public int Id { get; set; }
    public int Quantity { get; set; }

    [Precision(18, 2)]
    public decimal UnitPrice { get; set; }

    // Required FK — non-nullable int
    // DB column: "Order" (navigation name, not "OrderId")
    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;

    // Required FK — non-nullable int
    // DB column: "Product" (navigation name, not "ProductId")
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
}

// -----------------------------------------------------------------------------
// ProductReview — optional FK (nullable CustomerId), IAuditable
// -----------------------------------------------------------------------------

/// <summary>
/// A customer review for a product. Demonstrates an <b>optional</b> FK
/// relationship: <c>CustomerId</c> is <c>int?</c> (nullable), so the
/// convention builder calls <c>.IsRequired(false)</c> automatically —
/// a review can exist even if the customer account is deleted.
/// DB FK columns named <c>Product</c> (required) and <c>Customer</c>
/// (optional, nullable). Also implements <see cref="IAuditable"/>.
/// </summary>
public class ProductReview : IEntity, IAuditable
{
    public int Id { get; set; }
    public int Rating { get; set; }   // 1–5
    public string Comment { get; set; } = string.Empty;

    // Required FK — non-nullable int
    // DB column: "Product" (navigation name, not "ProductId")
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    // Optional FK — nullable int? → IsRequired(false) detected automatically
    // DB column: "Customer" (navigation name, not "CustomerId")
    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }

    // IAuditable — stamped automatically by AuditInterceptor
    public DateTime CreatedDate { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime? ModifiedDate { get; set; }
    public string? ModifiedBy { get; set; }
}