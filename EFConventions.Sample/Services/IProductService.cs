using EFConvention.Domain;

namespace EFConvention.Sample.Services
{
    // =============================================================================
    // ProductService — IAuditable + ISoftDelete + [Precision] decimal
    // =============================================================================

    public interface IProductService
    {
        Task<Product?> GetProductAsync(int id, CancellationToken ct = default);
        Task<IReadOnlyList<Product>> GetProductsByCategoryAsync(int categoryId, CancellationToken ct = default);
        Task<IReadOnlyList<Product>> GetDeletedProductsAsync(CancellationToken ct = default);
        Task<Product> AddProductAsync(Product product, CancellationToken ct = default);
        Task SaveProductAsync(Product product, CancellationToken ct = default);

        /// <summary>
        /// Soft-deletes the product. Product implements <see cref="ISoftDelete"/>
        /// so <see cref="ServiceBase{TEntity}.DeleteAsync"/> sets
        /// <c>IsDeleted = true</c> automatically. The product is hidden from
        /// normal queries but historical order data is preserved.
        /// </summary>
        Task DeleteProductAsync(int id, CancellationToken ct = default);

        Task RestoreProductAsync(int id, CancellationToken ct = default);
        Task PurgeProductAsync(int id, CancellationToken ct = default);
    }
}
