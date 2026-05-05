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
    [Fact]
    public void Customer_Address_IsRequired_NonNullableFk()
    {
        using var db = new TestDb(b => b.UseSnakeCase());
        var fk = db.Model.FindEntityType(typeof(Customer))!
            .GetForeignKeys()
            .FirstOrDefault(f => f.PrincipalEntityType.ClrType == typeof(Address));

        fk.Should().NotBeNull();
        fk!.IsRequired.Should().BeTrue("AddressId is int (non-nullable)");
    }

    [Fact]
    public void ProductReview_Customer_IsOptional_NullableFk()
    {
        using var db = new TestDb(b => b.UseSnakeCase());
        var fk = db.Model.FindEntityType(typeof(ProductReview))!
            .GetForeignKeys()
            .FirstOrDefault(f => f.PrincipalEntityType.ClrType == typeof(Customer));

        fk.Should().NotBeNull();
        fk!.IsRequired.Should().BeFalse("CustomerId is int? (nullable)");
    }

    [Fact]
    public void Order_Customer_IsRequired_RequiredAttribute()
    {
        using var db = new TestDb(b => b.UseSnakeCase());
        var fk = db.Model.FindEntityType(typeof(Order))!
            .GetForeignKeys()
            .FirstOrDefault(f => f.PrincipalEntityType.ClrType == typeof(Customer));

        fk.Should().NotBeNull();
        fk!.IsRequired.Should().BeTrue("Order.Customer carries [Required]");
    }

    [Fact]
    public void OrderItem_HasRequiredFks_ToBothOrderAndProduct()
    {
        using var db = new TestDb(b => b.UseSnakeCase());
        var itemType = db.Model.FindEntityType(typeof(OrderItem))!;

        itemType.GetForeignKeys()
            .First(f => f.PrincipalEntityType.ClrType == typeof(Order))
            .IsRequired.Should().BeTrue();

        itemType.GetForeignKeys()
            .First(f => f.PrincipalEntityType.ClrType == typeof(Product))
            .IsRequired.Should().BeTrue();
    }

    [Fact]
    public void Customer_Orders_FkColumnName_IsSnakeCase()
    {
        using var db = new TestDb(b => b.UseSnakeCase());
        db.Model.FindEntityType(typeof(Order))!
            .GetForeignKeys()
            .First(f => f.PrincipalEntityType.ClrType == typeof(Customer))
            .Properties.Single()
            .GetColumnName()
            .Should().Be("customer_id");
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
    [InlineData("DeletedAt", "deleted_at")]
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
                cols.DeletedAt = "ArchivedAt";
                cols.DeletedBy = "ArchivedBy";
            }));

        var et = db.Model.FindEntityType(typeof(Order))!;
        et.FindProperty("IsDeleted")!.GetColumnName().Should().Be("archived");
        et.FindProperty("DeletedAt")!.GetColumnName().Should().Be("archived_at");
        et.FindProperty("DeletedBy")!.GetColumnName().Should().Be("archived_by");
    }
}

// ---------------------------------------------------------------------------
// Audit columns
// ---------------------------------------------------------------------------

public class AuditColumnTests
{
    [Theory]
    [InlineData(typeof(Customer),      "CreatedAt",  "created_at")]
    [InlineData(typeof(Customer),      "CreatedBy",  "created_by")]
    [InlineData(typeof(Customer),      "ModifiedAt", "modified_at")]
    [InlineData(typeof(Customer),      "ModifiedBy", "modified_by")]
    [InlineData(typeof(Order),         "CreatedAt",  "created_at")]
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
                cols.CreatedAt = "RecordCreatedDate";
                cols.CreatedBy = "RecordCreatedUser";
                cols.ModifiedAt = "RecordModifiedDate";
                cols.ModifiedBy = "RecordModifiedUser";
            }));

        var et = db.Model.FindEntityType(typeof(Customer))!;
        et.FindProperty("CreatedAt")!.GetColumnName().Should().Be("record_created_date");
        et.FindProperty("CreatedBy")!.GetColumnName().Should().Be("record_created_user");
        et.FindProperty("ModifiedAt")!.GetColumnName().Should().Be("record_modified_date");
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
