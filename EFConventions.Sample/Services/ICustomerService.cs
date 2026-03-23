using EFConventions.Domain;
using System;
using System.Collections.Generic;
using System.Text;

namespace EFConventions.Sample.Services
{
    // -----------------------------------------------------------------------------
    // ICustomerService
    // -----------------------------------------------------------------------------

    public interface ICustomerService
    {
        Task<Customer?> GetCustomerAsync(int id, CancellationToken ct = default);
        Task<IReadOnlyList<Customer>> GetAllCustomersAsync(CancellationToken ct = default);
        Task<Customer> AddCustomerAsync(Customer customer, CancellationToken ct = default);
        Task SaveCustomerAsync(Customer customer, CancellationToken ct = default);

        /// <summary>
        /// Deletes the customer. Because <see cref="Customer"/> does not implement
        /// <see cref="ISoftDelete"/>, this is a <b>permanent hard delete</b>.
        /// Adding <see cref="ISoftDelete"/> to <see cref="Customer"/> automatically
        /// changes this to a soft delete with no code changes here.
        /// </summary>
        Task DeleteCustomerAsync(int id, CancellationToken ct = default);
    }
}
