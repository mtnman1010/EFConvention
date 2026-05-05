// =============================================================================
// EFConventions — Version 2.1
// Domain/Domain.cs
//
// Expanded domain model for the StoreDb example. Covers every feature of
// EntityConventionBuilder:
//
//   Address      — plain entity, no audit, no soft delete
//   Category     — plain entity (pluralisation example: Categories table)
//   Customer     — IAuditable only, hard delete
//   Product      — IAuditable + ISoftDelete, decimal with [Precision]
//   Order        — IAuditable + ISoftDelete, required FK, private collection
//   OrderItem    — plain entity, required FK to both Order and Product
//   ProductReview — optional FK (nullable CustomerId), IAuditable
//
// Navigation property rules enforced by the convention builder:
//   - Collection properties use 'private set;'  (public setter = startup error)
//   - Scalar FK properties are non-nullable int  → required relationship
//   - Scalar FK properties are nullable int?     → optional relationship
//   - [Required] on a navigation also forces required
//   - [Precision(18,2)] on decimal → applied automatically
// =============================================================================

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using EFConvention;
using Microsoft.EntityFrameworkCore;

namespace EFConvention.Domain;

// -----------------------------------------------------------------------------
// Address — plain entity, no audit, no soft delete
// -----------------------------------------------------------------------------

/// <summary>
/// A physical mailing address. Used by <see cref="Customer"/> as a required
/// reference navigation, demonstrating a required one-to-one-like relationship.
/// Plain entity — no audit fields, no soft delete.
/// </summary>
public class Address : IEntity
{
    public int    Id     { get; set; }
    public string Street { get; set; } = string.Empty;
    public string City   { get; set; } = string.Empty;
    public string State  { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
}

// -----------------------------------------------------------------------------
// Category — plain entity (demonstrates pluralised table name: Categories)
// -----------------------------------------------------------------------------

/// <summary>
/// Product category. Demonstrates that the pluralised convention correctly
/// handles the 'y' → 'ies' rule: <c>Category</c> → <c>Categories</c>.
/// Plain entity — no audit, no soft delete.
/// </summary>
public class Category : IEntity
{
    public int    Id          { get; set; }
    public string Name        { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    // Private setter — collection navigation, encapsulated
    public ICollection<Product> Products { get; private set; } = new List<Product>();
}

// -----------------------------------------------------------------------------
// Customer — IAuditable only, hard delete
// -----------------------------------------------------------------------------

/// <summary>
/// A store customer. Implements <see cref="IAuditable"/> — created/modified
/// fields are stamped automatically. Does NOT implement <see cref="ISoftDelete"/>
/// so <c>CustomerService.DeleteAsync</c> issues a physical DELETE.
///
/// <para>
/// <c>AddressId</c> is <c>int</c> (non-nullable) → required relationship.
/// The convention builder calls <c>.IsRequired(true)</c> automatically.
/// </para>
/// </summary>
public class Customer : IEntity, IAuditable
{
    public int    Id    { get; set; }
    public string Name  { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;

    // Required reference — non-nullable FK → convention builder marks as required
    public int     AddressId { get; set; }
    public Address Address   { get; set; } = null!;

    // Private setter — encapsulated collection
    public ICollection<Order>         Orders  { get; private set; } = new List<Order>();
    public ICollection<ProductReview> Reviews { get; private set; } = new List<ProductReview>();

    // IAuditable
    public DateTime  CreatedAt  { get; set; }
    public string    CreatedBy  { get; set; } = string.Empty;
    public DateTime? ModifiedAt { get; set; }
    public string?   ModifiedBy { get; set; }
}

// -----------------------------------------------------------------------------
// Product — IAuditable + ISoftDelete + [Precision] on decimal
// -----------------------------------------------------------------------------

/// <summary>
/// A store product. Demonstrates:
/// <list type="bullet">
///   <item><description>
///     <c>[Precision(18, 2)]</c> on <c>Price</c> — applied automatically by
///     the convention builder's decimal precision pass.
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
///   </description></item>
/// </list>
/// </summary>
public class Product : IEntity, IAuditable, ISoftDelete
{
    public int    Id          { get; set; }
    public string Name        { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Sku         { get; set; } = string.Empty;

    // [Precision] picked up automatically by ConfigureDecimalPrecision
    [Precision(18, 2)]
    public decimal Price { get; set; }

    [Precision(18, 2)]
    public decimal CostPrice { get; set; }

    // Required FK — non-nullable int
    public int      CategoryId { get; set; }
    public Category Category   { get; set; } = null!;

    // Private setter — encapsulated collection
    public ICollection<OrderItem>     OrderItems { get; private set; } = new List<OrderItem>();
    public ICollection<ProductReview> Reviews    { get; private set; } = new List<ProductReview>();

    // IAuditable
    public DateTime  CreatedAt  { get; set; }
    public string    CreatedBy  { get; set; } = string.Empty;
    public DateTime? ModifiedAt { get; set; }
    public string?   ModifiedBy { get; set; }

    // ISoftDelete
    public bool      IsDeleted  { get; set; }
    public DateTime? DeletedAt  { get; set; }
    public string?   DeletedBy  { get; set; }
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
    public int      Id          { get; set; }
    public DateTime OrderDate   { get; set; }
    public string   Status      { get; set; } = "Pending";

    [Precision(18, 2)]
    public decimal TotalAmount { get; set; }

    // [Required] on navigation — explicitly required regardless of FK type
    [Required]
    public int      CustomerId { get; set; }
    public Customer Customer   { get; set; } = null!;

    // Private setter — encapsulated collection
    public ICollection<OrderItem> Items { get; private set; } = new List<OrderItem>();

    // IAuditable
    public DateTime  CreatedAt  { get; set; }
    public string    CreatedBy  { get; set; } = string.Empty;
    public DateTime? ModifiedAt { get; set; }
    public string?   ModifiedBy { get; set; }

    // ISoftDelete
    public bool      IsDeleted  { get; set; }
    public DateTime? DeletedAt  { get; set; }
    public string?   DeletedBy  { get; set; }
}

// -----------------------------------------------------------------------------
// OrderItem — plain entity, required FKs to Order and Product
// -----------------------------------------------------------------------------

/// <summary>
/// A line item within an <see cref="Order"/>. Both FK properties are
/// non-nullable <c>int</c> → both relationships are detected as required
/// automatically. Plain entity — no audit, no soft delete.
/// </summary>
public class OrderItem : IEntity
{
    public int Id       { get; set; }
    public int Quantity { get; set; }

    [Precision(18, 2)]
    public decimal UnitPrice { get; set; }

    // Required FK — non-nullable int
    public int     OrderId { get; set; }
    public Order   Order   { get; set; } = null!;

    // Required FK — non-nullable int
    public int     ProductId { get; set; }
    public Product Product   { get; set; } = null!;
}

// -----------------------------------------------------------------------------
// ProductReview — optional FK (nullable CustomerId), IAuditable
// -----------------------------------------------------------------------------

/// <summary>
/// A customer review for a product. Demonstrates an <b>optional</b> FK
/// relationship: <c>CustomerId</c> is <c>int?</c> (nullable), so the
/// convention builder calls <c>.IsRequired(false)</c> automatically —
/// a review can exist even if the customer account is deleted.
/// Also implements <see cref="IAuditable"/>.
/// </summary>
public class ProductReview : IEntity, IAuditable
{
    public int    Id      { get; set; }
    public int    Rating  { get; set; }   // 1–5
    public string Comment { get; set; } = string.Empty;

    // Required FK — non-nullable int
    public int     ProductId { get; set; }
    public Product Product   { get; set; } = null!;

    // Optional FK — nullable int? → IsRequired(false) detected automatically
    public int?     CustomerId { get; set; }
    public Customer? Customer  { get; set; }

    // IAuditable
    public DateTime  CreatedAt  { get; set; }
    public string    CreatedBy  { get; set; } = string.Empty;
    public DateTime? ModifiedAt { get; set; }
    public string?   ModifiedBy { get; set; }
}
