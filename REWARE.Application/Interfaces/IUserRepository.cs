using REWEAR.Domain.Entities;

namespace REWARE.Application.Interfaces;

/// <summary>
/// Interface cho User Repository.
/// </summary>
public interface IUserRepository
{
    Task<User?> GetByIdAsync(string id);
    Task<User?> GetByEmailAsync(string email);
    Task<List<User>> GetAllAsync();
    Task CreateAsync(User user);
    Task UpdateAsync(User user);
    Task DeleteAsync(string id);
}
