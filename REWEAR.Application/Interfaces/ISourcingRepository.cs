using REWEAR.Domain.Entities;
using REWEAR.Domain.Enums;

namespace REWEAR.Application.Interfaces;

/// <summary>
/// Interface cho SourcingRequest Repository - data access layer.
/// </summary>
public interface ISourcingRepository
{
    /// <summary>
    /// Lấy sourcing request theo Id.
    /// </summary>
    Task<SourcingRequest?> GetByIdAsync(string id);

    /// <summary>
    /// Lấy tất cả sourcing request của một user.
    /// </summary>
    Task<List<SourcingRequest>> GetByUserIdAsync(string userId);

    /// <summary>
    /// Lấy tất cả sourcing request với filter theo status.
    /// </summary>
    /// <param name="status">null = tất cả, khác null = filter theo status</param>
    Task<List<SourcingRequest>> GetAllAsync(SourcingStatus? status = null);

    /// <summary>
    /// Tạo sourcing request mới.
    /// </summary>
    Task CreateAsync(SourcingRequest sourcingRequest);

    /// <summary>
    /// Cập nhật sourcing request.
    /// </summary>
    Task<bool> UpdateAsync(SourcingRequest sourcingRequest);
}
