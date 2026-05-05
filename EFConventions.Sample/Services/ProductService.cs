using EFConvention.Domain;
using Microsoft.EntityFrameworkCore;

namespace EFConvention.Sample.Services;

public sealed class ProductService : ServiceBase<Product>, IProductService
{
    public ProductService(IUnitOfWork unitOfWork, ICurrentUserService currentUser)
        : base(unitOfWork, currentUser) { }

    public async Task<Product?> GetProductAsync(int id, CancellationToken ct = default) =>
        // Global soft-delete filter excludes deleted products automatically.
        await UnitOfWork.Query<Product>()
            .Include(p => p.Category)
            .FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<IReadOnlyList<Product>> GetProductsByCategoryAsync(
        int categoryId, CancellationToken ct = default) =>
        await UnitOfWork.Query<Product>()
            .Where(p => p.CategoryId == categoryId)
            .OrderBy(p => p.Name)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Product>> GetDeletedProductsAsync(CancellationToken ct = default) =>
        // IgnoreQueryFilters() bypasses the global is_deleted = 0 filter.
        await UnitOfWork.Query<Product>()
            .IgnoreQueryFilters()
            .Where(p => p.IsDeleted)
            .OrderByDescending(p => p.DeletedAt)
            .ToListAsync(ct);

    public async Task<Product> AddProductAsync(Product product, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(product);
        if (string.IsNullOrWhiteSpace(product.Name))
            throw new ArgumentException("Product name is required.", nameof(product));
        if (product.Price <= 0)
            throw new ArgumentException("Price must be greater than zero.", nameof(product));

        UnitOfWork.Add(product);
        await UnitOfWork.CompleteAsync(ct);
        return product;
    }

    public async Task SaveProductAsync(Product product, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(product);
        var exists = await UnitOfWork.Query<Product>()
            .AnyAsync(p => p.Id == product.Id, ct);
        if (!exists)
            throw new KeyNotFoundException($"Product {product.Id} not found.");

        UnitOfWork.Update(product);
        await UnitOfWork.CompleteAsync(ct);
    }

    public async Task DeleteProductAsync(int id, CancellationToken ct = default)
    {
        var product = await UnitOfWork.Query<Product>()
            .FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new KeyNotFoundException($"Product {id} not found.");

        // Product implements ISoftDelete → soft delete path.
        await DeleteAsync(product, ct);
    }

    public async Task RestoreProductAsync(int id, CancellationToken ct = default)
    {
        var product = await UnitOfWork.Query<Product>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == id && p.IsDeleted, ct)
            ?? throw new KeyNotFoundException($"Soft-deleted product {id} not found.");

        await RestoreAsync(product, ct);
    }

    public async Task PurgeProductAsync(int id, CancellationToken ct = default)
    {
        var product = await UnitOfWork.Query<Product>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new KeyNotFoundException($"Product {id} not found.");

        await PurgeAsync(product, ct);
    }
}
