using REWEAR.Domain.Entities;

namespace REWEAR.Application.Interfaces;

/// <summary>
/// Interface cho Address Repository.
/// </summary>
public interface IAddressRepository
{
    Task<Address?> GetByIdAsync(string id);
    Task<List<Address>> GetByUserIdAsync(string userId);
    Task<Address?> GetDefaultByUserIdAsync(string userId);
    Task CreateAsync(Address address);
    Task UpdateAsync(Address address);
    Task DeleteAsync(string id);
    Task ClearDefaultForUserAsync(string userId);
}
