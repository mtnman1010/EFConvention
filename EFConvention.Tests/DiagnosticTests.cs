using EFConvention.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace EFConvention.Tests.Diagnostics
{
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
                .EnableServiceProviderCaching(false)
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
    // Diagnostics
    // ---------------------------------------------------------------------------

    public class EntityDiagnosticTests
    {
        [Fact]
        public void Diagnostic_ShowRegisteredConventions()
        {
            using var db = new TestDb(_ => { });
            var conventions = db.Model.GetAnnotations()
                .Select(a => $"{a.Name} = {a.Value}")
                .ToList();

            // EF Core registers exactly one annotation — its version number
            conventions.Should().ContainSingle()
                .Which.Should().StartWith("ProductVersion");
        }

        [Fact]
        public void Diagnostic_EFCoreDefaultTableName()
        {
            using var ctx = new DbContext(
                new DbContextOptionsBuilder<DbContext>()
                    .UseInMemoryDatabase(Guid.NewGuid().ToString())
                    .Options);

            // A plain DbContext with no OnModelCreating has no knowledge of Customer
            // — FindEntityType returns null as expected
            var tableName = ctx.Model.FindEntityType(typeof(Customer))?.GetTableName();
            tableName.Should().BeNull();
        }

        [Fact]
        public void Diagnostic_WhatNamingConventionProduces()
        {
            var convention = new PascalCaseNamingConvention();
            var tableName = convention.GetTableName(typeof(Customer));
            tableName.Should().Be("Customer");
            var columnName = convention.GetColumnName(typeof(Customer).GetProperty("Name")!);
            columnName.Should().Be("Name");
        }

        [Fact]
        public void Diagnostic_BuilderWithNoConfigProducesPascalCase()
        {
            var builder = EntityConventionBuilder.ForAssemblyOf<Customer>();
            // Don't call any convention method — default should be PascalCase

            using var db = new DbContext(
                new DbContextOptionsBuilder<DbContext>()
                    .UseInMemoryDatabase(Guid.NewGuid().ToString())
                    .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                    .Options);

            var modelBuilder = new ModelBuilder();
            builder.Apply(modelBuilder);

            var tableName = modelBuilder.Model
                .FindEntityType(typeof(Customer))?.GetTableName();

            tableName.Should().Be("Customer");
        }
    }
}
