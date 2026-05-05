namespace EFConvention
{
    // -----------------------------------------------------------------------------
    // Entity identity contracts
    //
    //   IEntityBase           — discovery marker for EntityConventionBuilder
    //   IEntity               — typed identity (int PK)
    // -----------------------------------------------------------------------------



    /// <summary>
    /// Root marker interface. Any class implementing <see cref="IEntityBase"/>
    /// (directly or via <see cref="IEntity"/>) is discovered automatically by
    /// <see cref="EntityConventionBuilder"/> and registered as an EF Core entity.
    ///
    /// <para>
    /// Using an interface rather than an abstract base class means domain objects
    /// are not forced into a single inheritance chain — a class that already
    /// inherits from a third-party base type can still participate by implementing
    /// this interface.
    /// </para>
    /// </summary>
    public interface IEntityBase { }

    /// <summary>
    /// Standard entity contract. Extends <see cref="IEntityBase"/> with a typed
    /// integer primary key. Most domain entities implement this directly.
    /// </summary>
    public interface IEntity : IEntityBase
    {
        /// <summary>Database-generated integer primary key.</summary>
        int Id { get; set; }
    }
}
