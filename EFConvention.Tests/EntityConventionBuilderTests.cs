// =============================================================================
// EFConvention.Tests
// Builder/EntityConventionBuilderTests.cs
//
// Unit tests for EntityConventionBuilder. All tests use EF's InMemory
// provider to inspect the built model — no SQL Server required.
//
// Suites:
//   EntityDiscoveryTests       — IEntityBase discovery
//   NamingConventionTests      — PascalCase, snake_case, pluralised
//   RelationshipTests          — required/optional FK detection
//   DecimalPrecisionTests      — [Precision] attribute pickup
//   SoftDeleteTests            — global query filter registration
//   AuditColumnTests           — column name mapping and overrides
//   StartupValidationTests     — public setter error, collect-all errors
// =============================================================================

using EFConvention.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.ComponentModel.DataAnnotations;
using Xunit;

namespace EFConvention.Tests.Builder;

// ---------------------------------------------------------------------------
// Minimal test DbContext — applies the builder with a caller-supplied config
// ---------------------------------------------------------------------------

file sealed class TestDb : DbContext
{
    private readonly Action<EntityConventionBuilder> _configure;

    public TestDb(Action<EntityConventionBuilder> configure)
    : base(new DbContextOptionsBuilder<TestDb>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
        .EnableServiceProviderCaching(false)  // ← add this
        .Options)
    => _configure = configure;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var b = EntityConventionBuilder.ForAssemblyOf<Customer>();
        _configure(b);
        base.OnModelCreating(modelBuilder);
        b.Apply(modelBuilder);
    }
}

// ---------------------------------------------------------------------------
// Discovery
// ---------------------------------------------------------------------------

public class EntityDiscoveryTests
{
    [Fact]
    public void Apply_RegistersAllIEntityBaseTypes()
    {
        using var db = new TestDb(b => b.UseSnakeCase());
        var model    = db.Model;

        model.FindEntityType(typeof(Address))      .Should().NotBeNull();
        model.FindEntityType(typeof(Category))     .Should().NotBeNull();
        model.FindEntityType(typeof(Customer))     .Should().NotBeNull();
        model.FindEntityType(typeof(Product))      .Should().NotBeNull();
        model.FindEntityType(typeof(Order))        .Should().NotBeNull();
        model.FindEntityType(typeof(OrderItem))    .Should().NotBeNull();
        model.FindEntityType(typeof(ProductReview)).Should().NotBeNull();
    }

    [Fact]
    public void Apply_DoesNotRegisterInterfaces()
    {
        using var db = new TestDb(b => b.UseSnakeCase());
        db.Model.FindEntityType(typeof(IEntityBase)).Should().BeNull();
        db.Model.FindEntityType(typeof(IEntity))    .Should().BeNull();
    }
}

// ---------------------------------------------------------------------------
// Naming conventions
// ---------------------------------------------------------------------------

public class NamingConventionTests
{
    [Theory]
    [InlineData(typeof(Customer),      "Customer")]
    [InlineData(typeof(OrderItem),     "OrderItem")]
    [InlineData(typeof(ProductReview), "ProductReview")]
    public void PascalCase_TableNames_MatchTypeName(Type t, string expected)
    {
        using var db = new TestDb(_ => { }); // default = PascalCase
        db.Model.FindEntityType(t)!.GetTableName().Should().Be(expected);
    }

    [Theory]
    [InlineData(typeof(Customer),      "customer")]
    [InlineData(typeof(OrderItem),     "order_item")]
    [InlineData(typeof(ProductReview), "product_review")]
    public void SnakeCase_TableNames_AreConverted(Type t, string expected)
    {
        using var db = new TestDb(b => b.UseSnakeCase());
        db.Model.FindEntityType(t)!.GetTableName().Should().Be(expected);
    }

    [Theory]
    [InlineData(typeof(Customer),  "Customers")]
    [InlineData(typeof(Category),  "Categories")]   // y → ies
    [InlineData(typeof(Address),   "Addresses")]    // s → es
    [InlineData(typeof(OrderItem), "OrderItems")]
    public void PluralizedTables_AppliesEnglishRules(Type t, string expected)
    {
        using var db = new TestDb(b => b.UsePluralizedTables());
        db.Model.FindEntityType(t)!.GetTableName().Should().Be(expected);
    }

    [Theory]
    [InlineData(typeof(Customer), "Name",      "name")]
    [InlineData(typeof(Order),    "OrderDate", "order_date")]
    [InlineData(typeof(Order),    "Status",    "status")]
    [InlineData(typeof(Product),  "Price",     "price")]
    public void SnakeCase_ColumnNames_AreConverted(Type t, string prop, string expected)
    {
        using var db = new TestDb(b => b.UseSnakeCase());
        db.Model.FindEntityType(t)!
            .FindProperty(prop)!
            .GetColumnName()
            .Should().Be(expected);
    }
}

// ---------------------------------------------------------------------------
// Relationship detection
// ---------------------------------------------------------------------------

public class RelationshipTests
{
    // -------------------------------------------------------------------------
    // Required / optional detection — scalar FK property (backwards compatible)
    // -------------------------------------------------------------------------

    [Fact]
    public void Customer_Address_IsRequired_NonNullableScalarFk()
    {
        using var db = new TestDb(b => b.UseSnakeCase());
        var fk = db.Model.FindEntityType(typeof(Customer))!
            .GetForeignKeys()
            .FirstOrDefault(f => f.PrincipalEntityType.ClrType == typeof(Address));

        fk.Should().NotBeNull();
        fk!.IsRequired.Should().BeTrue(
            "AddressId is int (non-nullable scalar FK) → required");
    }

    [Fact]
    public void ProductReview_Customer_IsOptional_NullableScalarFk()
    {
        using var db = new TestDb(b => b.UseSnakeCase());
        var fk = db.Model.FindEntityType(typeof(ProductReview))!
            .GetForeignKeys()
            .FirstOrDefault(f => f.PrincipalEntityType.ClrType == typeof(Customer));

        fk.Should().NotBeNull();
        fk!.IsRequired.Should().BeFalse(
            "CustomerId is int? (nullable scalar FK) → optional");
    }

    [Fact]
    public void Order_Customer_IsRequired_RequiredAttribute()
    {
        using var db = new TestDb(b => b.UseSnakeCase());
        var fk = db.Model.FindEntityType(typeof(Order))!
            .GetForeignKeys()
            .FirstOrDefault(f => f.PrincipalEntityType.ClrType == typeof(Customer));

        fk.Should().NotBeNull();
        fk!.IsRequired.Should().BeTrue(
            "Order.Customer carries [Required] → required regardless of FK type");
    }

    [Fact]
    public void OrderItem_HasRequiredFks_ToBothOrderAndProduct()
    {
        using var db = new TestDb(b => b.UseSnakeCase());
        var itemType = db.Model.FindEntityType(typeof(OrderItem))!;

        itemType.GetForeignKeys()
            .First(f => f.PrincipalEntityType.ClrType == typeof(Order))
            .IsRequired.Should().BeTrue(
                "OrderId is int (non-nullable scalar FK) → required");

        itemType.GetForeignKeys()
            .First(f => f.PrincipalEntityType.ClrType == typeof(Product))
            .IsRequired.Should().BeTrue(
                "ProductId is int (non-nullable scalar FK) → required");
    }

    // -------------------------------------------------------------------------
    // Required / optional detection — nullable reference type (v2.3)
    // -------------------------------------------------------------------------

    private sealed class PrincipalNrt : IEntity
    {
        public int Id { get; set; }

        // "RequiredDependents".StartsWith("Required") → maps to Required nav
        public ICollection<DependentNrt> RequiredDependents
        { get; private set; } = new List<DependentNrt>();

        // "OptionalPrincipalDependents".StartsWith("OptionalPrincipal") → maps to OptionalPrincipal nav
        public ICollection<DependentNrt> OptionalPrincipalDependents
        { get; private set; } = new List<DependentNrt>();
    }

    private sealed class DependentNrt : IEntity
    {
        public int Id { get; set; }

        // Non-nullable → required (no scalar FK property)
        public PrincipalNrt Required { get; set; } = null!;

        // Nullable → optional (no scalar FK property)
        public PrincipalNrt? OptionalPrincipal { get; set; }
    }

    private sealed class NrtDb : DbContext
    {
        public NrtDb() : base(
            new DbContextOptionsBuilder<NrtDb>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .EnableServiceProviderCaching(false)
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options)
        { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            EntityConventionBuilder
                .ForTypes(typeof(PrincipalNrt), typeof(DependentNrt))
                .Apply(modelBuilder);
        }
    }

    [Fact]
    public void NonNullableNavigation_NoScalarFk_IsRequired()
    {
        using var db = new NrtDb();
        var fk = db.Model.FindEntityType(typeof(DependentNrt))!
            .GetForeignKeys()
            .FirstOrDefault(f => f.DependentToPrincipal?.Name == "Required");

        fk.Should().NotBeNull();
        fk!.IsRequired.Should().BeTrue(
            "non-nullable navigation without scalar FK → required via NullabilityInfoContext");
    }

    [Fact]
    public void NullableNavigation_NoScalarFk_IsOptional()
    {
        using var db = new NrtDb();
        var fk = db.Model.FindEntityType(typeof(DependentNrt))!
            .GetForeignKeys()
            .FirstOrDefault(f => f.DependentToPrincipal?.Name == "OptionalPrincipal");

        fk.Should().NotBeNull();
        fk!.IsRequired.Should().BeFalse(
            "nullable navigation without scalar FK → optional via NullabilityInfoContext");
    }

    [Fact]
    public void NoScalarFk_FkColumnNamedAfterNavigation()
    {
        using var db = new NrtDb();
        var fk = db.Model.FindEntityType(typeof(DependentNrt))!
            .GetForeignKeys()
            .FirstOrDefault(f => f.DependentToPrincipal?.Name == "Required");

        fk!.Properties.Single().GetColumnName()
            .Should().Be("Required",
                "FK column named after navigation property even without scalar FK");
    }

    [Fact]
    public void NoScalarFk_ModelBuilds_WithoutError()
    {
        var act = () =>
        {
            using var db = new NrtDb();
            _ = db.Model;
        };

        act.Should().NotThrow(
            "domain objects without scalar FK properties should build cleanly");
    }

    // -------------------------------------------------------------------------
    // FK column naming — navigation property name, not TypeId
    // -------------------------------------------------------------------------

    [Fact]
    public void Customer_Orders_FkColumn_IsNavigationName_SnakeCase()
    {
        using var db = new TestDb(b => b.UseSnakeCase());
        db.Model.FindEntityType(typeof(Order))!
            .GetForeignKeys()
            .First(f => f.PrincipalEntityType.ClrType == typeof(Customer))
            .Properties.Single()
            .GetColumnName()
            .Should().Be("customer",
                "FK column named after navigation property 'Customer' → snake_case 'customer'");
    }

    [Fact]
    public void Customer_Address_FkColumn_IsNavigationName_SnakeCase()
    {
        using var db = new TestDb(b => b.UseSnakeCase());
        db.Model.FindEntityType(typeof(Customer))!
            .GetForeignKeys()
            .First(f => f.PrincipalEntityType.ClrType == typeof(Address))
            .Properties.Single()
            .GetColumnName()
            .Should().Be("address",
                "FK column named after navigation property 'Address' → snake_case 'address'");
    }

    [Fact]
    public void OrderItem_FkColumns_AreNavigationNames_SnakeCase()
    {
        using var db = new TestDb(b => b.UseSnakeCase());
        var itemType = db.Model.FindEntityType(typeof(OrderItem))!;

        itemType.GetForeignKeys()
            .First(f => f.PrincipalEntityType.ClrType == typeof(Order))
            .Properties.Single()
            .GetColumnName()
            .Should().Be("order",
                "FK column named after navigation property 'Order' → snake_case 'order'");

        itemType.GetForeignKeys()
            .First(f => f.PrincipalEntityType.ClrType == typeof(Product))
            .Properties.Single()
            .GetColumnName()
            .Should().Be("product",
                "FK column named after navigation property 'Product' → snake_case 'product'");
    }

    [Fact]
    public void ProductReview_FkColumns_AreNavigationNames_SnakeCase()
    {
        using var db = new TestDb(b => b.UseSnakeCase());
        var reviewType = db.Model.FindEntityType(typeof(ProductReview))!;

        reviewType.GetForeignKeys()
            .First(f => f.PrincipalEntityType.ClrType == typeof(Product))
            .Properties.Single()
            .GetColumnName()
            .Should().Be("product");

        reviewType.GetForeignKeys()
            .First(f => f.PrincipalEntityType.ClrType == typeof(Customer))
            .Properties.Single()
            .GetColumnName()
            .Should().Be("customer");
    }
}

// ---------------------------------------------------------------------------
// Decimal precision
// ---------------------------------------------------------------------------

public class DecimalPrecisionTests
{
    [Theory]
    [InlineData(typeof(Product),   "Price",       18, 2)]
    [InlineData(typeof(Product),   "CostPrice",   18, 2)]
    [InlineData(typeof(Order),     "TotalAmount", 18, 2)]
    [InlineData(typeof(OrderItem), "UnitPrice",   18, 2)]
    public void PrecisionAttribute_IsApplied(Type t, string prop, int precision, int scale)
    {
        using var db = new TestDb(b => b.UseSnakeCase());
        var p = db.Model.FindEntityType(t)!.FindProperty(prop)!;
        p.GetPrecision().Should().Be(precision);
        p.GetScale()    .Should().Be(scale);
    }
}

// ---------------------------------------------------------------------------
// Soft delete
// ---------------------------------------------------------------------------

public class SoftDeleteTests
{
    [Fact]
    public void WithSoftDelete_RegistersQueryFilter_OnISoftDeleteEntities()
    {
        using var db = new TestDb(b => b.UseSnakeCase().WithSoftDelete());

        db.Model.FindEntityType(typeof(Order))!  .GetDeclaredQueryFilters().Should().NotBeNull();
        db.Model.FindEntityType(typeof(Product))!.GetDeclaredQueryFilters().Should().NotBeNull();
    }

    [Fact]
    public void WithoutSoftDelete_NoQueryFilter()
    {
        using var db = new TestDb(b => b.UseSnakeCase());
        db.Model.FindEntityType(typeof(Order))!.GetDeclaredQueryFilters().Should().BeEmpty();
    }

    [Fact]
    public void PlainEntity_NeverGetsQueryFilter()
    {
        using var db = new TestDb(b => b.UseSnakeCase().WithSoftDelete());
        db.Model.FindEntityType(typeof(Address))!.GetDeclaredQueryFilters().Should().BeEmpty();
        db.Model.FindEntityType(typeof(Category))!.GetDeclaredQueryFilters().Should().BeEmpty();
    }

    [Theory]
    [InlineData("IsDeleted", "is_deleted")]
    [InlineData("DeletedDate", "deleted_date")]
    [InlineData("DeletedBy", "deleted_by")]
    public void SoftDeleteColumns_FollowSnakeCaseConvention(string prop, string expected)
    {
        using var db = new TestDb(b => b.UseSnakeCase().WithSoftDelete());
        db.Model.FindEntityType(typeof(Order))!
            .FindProperty(prop)!
            .GetColumnName()
            .Should().Be(expected);
    }

    [Fact]
    public void SoftDeleteColumnNames_CanBeOverridden()
    {
        using var db = new TestDb(b => b
            .UseSnakeCase()
            .WithSoftDelete(cols =>
            {
                cols.IsDeleted = "Archived";
                cols.DeletedDate = "ArchivedDate";
                cols.DeletedBy = "ArchivedBy";
            }));

        var et = db.Model.FindEntityType(typeof(Order))!;
        et.FindProperty("IsDeleted")!.GetColumnName().Should().Be("archived");
        et.FindProperty("DeletedDate")!.GetColumnName().Should().Be("archived_date");
        et.FindProperty("DeletedBy")!.GetColumnName().Should().Be("archived_by");
    }
}

// ---------------------------------------------------------------------------
// Audit columns
// ---------------------------------------------------------------------------

public class AuditColumnTests
{
    [Theory]
    [InlineData(typeof(Customer),      "CreatedDate",  "created_date")]
    [InlineData(typeof(Customer),      "CreatedBy",  "created_by")]
    [InlineData(typeof(Customer),      "ModifiedDate", "modified_date")]
    [InlineData(typeof(Customer),      "ModifiedBy", "modified_by")]
    [InlineData(typeof(Order),         "CreatedDate",  "created_date")]
    [InlineData(typeof(ProductReview), "CreatedBy",  "created_by")]
    public void AuditColumns_FollowSnakeCaseConvention(Type t, string prop, string expected)
    {
        using var db = new TestDb(b => b.UseSnakeCase().WithAuditFields());
        db.Model.FindEntityType(t)!
            .FindProperty(prop)!
            .GetColumnName()
            .Should().Be(expected);
    }

    [Fact]
    public void AuditColumns_CanBeOverridden()
    {
        using var db = new TestDb(b => b
            .UseSnakeCase()
            .WithAuditFields(cols =>
            {
                cols.CreatedDate = "RecordCreatedDate";
                cols.CreatedBy = "RecordCreatedUser";
                cols.ModifiedDate = "RecordModifiedDate";
                cols.ModifiedBy = "RecordModifiedUser";
            }));

        var et = db.Model.FindEntityType(typeof(Customer))!;
        et.FindProperty("CreatedDate")!.GetColumnName().Should().Be("record_created_date");
        et.FindProperty("CreatedBy")!.GetColumnName().Should().Be("record_created_user");
        et.FindProperty("ModifiedDate")!.GetColumnName().Should().Be("record_modified_date");
        et.FindProperty("ModifiedBy")!.GetColumnName().Should().Be("record_modified_user");
    }

    [Fact]
    public void PlainEntity_HasNoAuditColumns()
    {
        using var db = new TestDb(b => b.UseSnakeCase().WithAuditFields());
        var et = db.Model.FindEntityType(typeof(Address))!;
        et.FindProperty("CreatedAt").Should().BeNull();
        et.FindProperty("CreatedBy").Should().BeNull();
    }
}

// ---------------------------------------------------------------------------
// Startup validation
// ---------------------------------------------------------------------------

public class StartupValidationTests
{
    // A domain entity with a deliberately public collection setter
    private sealed class BadEntity : IEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        // PUBLIC setter — should trigger a startup error
        public ICollection<GoodEntity> Children { get; set; } = new List<GoodEntity>();
    }

    private sealed class GoodEntity : IEntity
    {
        public int Id { get; set; }
        public BadEntity Parent { get; set; } = null!;
    }

    private sealed class ValidationDb : DbContext
    {
        public ValidationDb() : base(
            new DbContextOptionsBuilder<ValidationDb>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            EntityConventionBuilder
                .ForAssembly(typeof(BadEntity).Assembly)
                .UseSnakeCase()
                .Apply(modelBuilder);
    }

    [Fact]
    public void Apply_Throws_WhenCollectionHasPublicSetter()
    {
        var act = () =>
        {
            using var db = new ValidationDb();
            _ = db.Model; // triggers OnModelCreating
        };

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*public setter*");
    }

    [Fact]
    public void Apply_ErrorMessage_IncludesEntityAndPropertyName()
    {
        var act = () =>
        {
            using var db = new ValidationDb();
            _ = db.Model;
        };

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*BadEntity*")
            .WithMessage("*Children*");
    }

    [Fact]
    public void Apply_Succeeds_WhenAllCollectionsHavePrivateSetters()
    {
        // The sample domain entities all use private setters — no errors expected
        var act = () =>
        {
            using var db = new TestDb(b => b.UseSnakeCase().WithFullAudit());
            _ = db.Model;
        };

        act.Should().NotThrow();
    }
}

public class RelationshipDisambiguationTests
{
    // -------------------------------------------------------------------------
    // Multiple collections of the same type — GetCollection StartsWith matching
    // -------------------------------------------------------------------------

    // Simulates the Person/Withholding scenario from real-world usage
    private sealed class Principal : IEntity
    {
        public int Id { get; set; }

        // Three collections of the same dependent type
        // Each named to start with the corresponding navigation name
        public ICollection<Dependent> PrimaryDependents
        { get; private set; } = new List<Dependent>();
        public ICollection<Dependent> SecondaryDependents
        { get; private set; } = new List<Dependent>();
        public ICollection<Dependent> TertiaryDependents
        { get; private set; } = new List<Dependent>();
    }

    private sealed class Dependent : IEntity
    {
        public int Id { get; set; }

        // Three navigations back to the same principal type
        [Required]
        public Principal Primary { get; set; } = null!;
        [Required]
        public Principal Secondary { get; set; } = null!;
        public Principal Tertiary { get; set; } = null!;
    }

    private sealed class DisambiguationDb : DbContext
    {
        public DisambiguationDb() : base(
            new DbContextOptionsBuilder<DisambiguationDb>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .EnableServiceProviderCaching(false)
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options)
        { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            EntityConventionBuilder
                .ForTypes(typeof(Principal), typeof(Dependent))
                .UseSnakeCase()
                .Apply(modelBuilder);
        }
    }

    [Fact]
    public void MultipleCollections_SameType_ResolvedByStartsWith()
    {
        // Should not throw — if disambiguation fails EF Core throws
        // about duplicate relationship configuration
        var act = () =>
        {
            using var db = new DisambiguationDb();
            _ = db.Model;
        };

        act.Should().NotThrow(
            "GetCollection should disambiguate multiple collections of the " +
            "same type using StartsWith convention");
    }

    [Fact]
    public void MultipleCollections_PrimaryNav_MapsToCorrectCollection()
    {
        using var db = new DisambiguationDb();
        var depType = db.Model.FindEntityType(typeof(Dependent))!;

        // Primary navigation → PrimaryDependents collection
        var primaryFk = depType.GetForeignKeys()
            .FirstOrDefault(f => f.DependentToPrincipal?.Name == "Primary");

        primaryFk.Should().NotBeNull();
        primaryFk!.PrincipalToDependent?.Name
            .Should().Be("PrimaryDependents",
                "Primary nav should map to PrimaryDependents collection");
    }

    [Fact]
    public void MultipleCollections_SecondaryNav_MapsToCorrectCollection()
    {
        using var db = new DisambiguationDb();
        var depType = db.Model.FindEntityType(typeof(Dependent))!;

        var secondaryFk = depType.GetForeignKeys()
            .FirstOrDefault(f => f.DependentToPrincipal?.Name == "Secondary");

        secondaryFk.Should().NotBeNull();
        secondaryFk!.PrincipalToDependent?.Name
            .Should().Be("SecondaryDependents",
                "Secondary nav should map to SecondaryDependents collection");
    }

    [Fact]
    public void MultipleCollections_TertiaryNav_MapsToCorrectCollection()
    {
        using var db = new DisambiguationDb();
        var depType = db.Model.FindEntityType(typeof(Dependent))!;

        var tertiaryFk = depType.GetForeignKeys()
            .FirstOrDefault(f => f.DependentToPrincipal?.Name == "Tertiary");

        tertiaryFk.Should().NotBeNull();
        tertiaryFk!.PrincipalToDependent?.Name
            .Should().Be("TertiaryDependents",
                "Tertiary nav should map to TertiaryDependents collection");
    }

    // -------------------------------------------------------------------------
    // FK column naming — navigation name not TypeId
    // -------------------------------------------------------------------------

    [Fact]
    public void FkColumn_NamedAfterNavigation_NotTypeId()
    {
        using var db = new DisambiguationDb();
        var depType = db.Model.FindEntityType(typeof(Dependent))!;

        // FK column for Primary navigation should be "primary" not "primary_id"
        var primaryFk = depType.GetForeignKeys()
            .First(f => f.DependentToPrincipal?.Name == "Primary");

        primaryFk.Properties.Single().GetColumnName()
            .Should().Be("primary",
                "FK column should be named after the navigation property, not TypeId");
    }

    [Fact]
    public void FkColumn_MultipleNavsToSameType_AllNamedCorrectly()
    {
        using var db = new DisambiguationDb();
        var depType = db.Model.FindEntityType(typeof(Dependent))!;

        var fksByNav = depType.GetForeignKeys()
            .Where(f => f.DependentToPrincipal != null)
            .ToDictionary(
                f => f.DependentToPrincipal!.Name,
                f => f.Properties.Single().GetColumnName());

        fksByNav["Primary"].Should().Be("primary");
        fksByNav["Secondary"].Should().Be("secondary");
        fksByNav["Tertiary"].Should().Be("tertiary");
    }
}

public class NullableReferenceTypeTests
{
    private sealed class PrincipalNrt : IEntity
    {
        public int Id { get; set; }

        // Named to match StartsWith disambiguation convention
        // "RequiredDependents".StartsWith("Required") → Required nav
        public ICollection<DependentNrt> RequiredDependents
        { get; private set; } = new List<DependentNrt>();

        // "OptionalPrincipalDependents".StartsWith("OptionalPrincipal") → OptionalPrincipal nav
        public ICollection<DependentNrt> OptionalPrincipalDependents
        { get; private set; } = new List<DependentNrt>();
    }

    private sealed class DependentNrt : IEntity
    {
        public int Id { get; set; }

        // Non-nullable → required (no scalar FK property)
        public PrincipalNrt Required { get; set; } = null!;

        // Nullable → optional (no scalar FK property)
        public PrincipalNrt? OptionalPrincipal { get; set; }
    }

    private sealed class NrtDb : DbContext
    {
        public NrtDb() : base(
            new DbContextOptionsBuilder<NrtDb>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .EnableServiceProviderCaching(false)
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options)
        { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            EntityConventionBuilder
                .ForTypes(typeof(PrincipalNrt), typeof(DependentNrt))
                .Apply(modelBuilder);
        }
    }

    [Fact]
    public void ModelBuilds_WithoutError()
    {
        var act = () =>
        {
            using var db = new NrtDb();
            _ = db.Model;
        };

        act.Should().NotThrow(
            "domain objects without scalar FK properties should build cleanly");
    }

    [Fact]
    public void NonNullableNavigation_NoScalarFk_IsRequired()
    {
        using var db = new NrtDb();
        var fk = db.Model.FindEntityType(typeof(DependentNrt))!
            .GetForeignKeys()
            .FirstOrDefault(f => f.DependentToPrincipal?.Name == "Required");

        fk.Should().NotBeNull();
        fk!.IsRequired.Should().BeTrue(
            "non-nullable navigation without scalar FK → required via NullabilityInfoContext");
    }

    [Fact]
    public void NullableNavigation_NoScalarFk_IsOptional()
    {
        using var db = new NrtDb();
        var fk = db.Model.FindEntityType(typeof(DependentNrt))!
            .GetForeignKeys()
            .FirstOrDefault(f => f.DependentToPrincipal?.Name == "OptionalPrincipal");

        fk.Should().NotBeNull();
        fk!.IsRequired.Should().BeFalse(
            "nullable navigation without scalar FK → optional via NullabilityInfoContext");
    }

    [Fact]
    public void NonNullableNavigation_FkColumnNamedAfterNavigation()
    {
        using var db = new NrtDb();
        var fk = db.Model.FindEntityType(typeof(DependentNrt))!
            .GetForeignKeys()
            .FirstOrDefault(f => f.DependentToPrincipal?.Name == "Required");

        fk!.Properties.Single().GetColumnName()
            .Should().Be("Required",
                "FK column named after navigation property even without scalar FK");
    }

    [Fact]
    public void StartsWith_Disambiguation_WorksAlongsideNullableReferenceTypes()
    {
        using var db = new NrtDb();
        var depType = db.Model.FindEntityType(typeof(DependentNrt))!;

        // Required nav → RequiredDependents collection
        depType.GetForeignKeys()
            .First(f => f.DependentToPrincipal?.Name == "Required")
            .PrincipalToDependent?.Name
            .Should().Be("RequiredDependents");

        // OptionalPrincipal nav → OptionalPrincipalDependents collection
        depType.GetForeignKeys()
            .First(f => f.DependentToPrincipal?.Name == "OptionalPrincipal")
            .PrincipalToDependent?.Name
            .Should().Be("OptionalPrincipalDependents");
    }
}
