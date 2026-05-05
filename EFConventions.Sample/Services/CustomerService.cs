using EFConvention.Domain;
using Microsoft.EntityFrameworkCore;

namespace EFConvention.Sample.Services
{
    // =============================================================================
    // EFConventions — Version 2.1
    // Services/CustomerService.cs
    //
    // Customer service — hard delete (Customer does not implement ISoftDelete).
    // Demonstrates: IUnitOfWork.Query, Add, FindAsync, Update, DeleteAsync (hard).
    // =============================================================================

    // -----------------------------------------------------------------------------
    // CustomerService
    // -----------------------------------------------------------------------------

    public sealed class CustomerService : ServiceBase<Customer>, ICustomerService
    {
        public CustomerService(IUnitOfWork unitOfWork, ICurrentUserService currentUser)
            : base(unitOfWork, currentUser) { }

        public async Task<Customer?> GetCustomerAsync(int id, CancellationToken ct = default) =>
            await UnitOfWork.Query<Customer>()
                .Include(c => c.Address)
                .Include(c => c.Orders)
                .Include(c => c.Reviews)
                .FirstOrDefaultAsync(c => c.Id == id, ct);

        public async Task<IReadOnlyList<Customer>> GetAllCustomersAsync(CancellationToken ct = default) =>
            await UnitOfWork.Query<Customer>()
                .OrderBy(c => c.Name)
                .ToListAsync(ct);

        public async Task<Customer> AddCustomerAsync(Customer customer, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(customer);
            if (string.IsNullOrWhiteSpace(customer.Name))
                throw new ArgumentException("Customer name is required.", nameof(customer));

            UnitOfWork.Add(customer);
            await UnitOfWork.CompleteAsync(ct);
            return customer;
        }

        public async Task SaveCustomerAsync(Customer customer, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(customer);
            if (string.IsNullOrWhiteSpace(customer.Name))
                throw new ArgumentException("Customer name is required.", nameof(customer));

            var exists = await UnitOfWork.Query<Customer>()
                .AnyAsync(c => c.Id == customer.Id, ct);
            if (!exists)
                throw new KeyNotFoundException($"Customer {customer.Id} not found.");

            UnitOfWork.Update(customer);
            await UnitOfWork.CompleteAsync(ct);
        }

        public async Task DeleteCustomerAsync(int id, CancellationToken ct = default)
        {
            var customer = await UnitOfWork.Query<Customer>()
                .FirstOrDefaultAsync(c => c.Id == id, ct)
                ?? throw new KeyNotFoundException($"Customer {id} not found.");

            // ServiceBase.DeleteAsync inspects ISoftDelete at runtime.
            // Customer does not implement it → physical DELETE.
            await DeleteAsync(customer, ct);
        }
    }
}
