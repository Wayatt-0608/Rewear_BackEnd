using REWEAR.Domain.Entities;
using REWEAR.Domain.Enums;

namespace REWEAR.Application.Interfaces;

/// <summary>
/// Repository cho Shipping (Task 9 + Task Shipper).
/// </summary>
public interface IShippingRepository
{
    Task CreateAsync(Shipping shipping);

    Task UpdateAsync(Shipping shipping);

    /// <summary>Lấy thông tin vận chuyển của một đơn (mỗi đơn chỉ có 1 bản ghi).</summary>
    Task<Shipping?> GetByOrderIdAsync(string orderId);

    /// <summary>Lấy danh sách vận chuyển của một shipper, có thể filter theo trạng thái.</summary>
    Task<List<Shipping>> GetByShipperIdAsync(string shipperId, ShippingStatus? status);

    /// <summary>Lấy toàn bộ vận chuyển, có thể filter theo trạng thái (AdminOnly dashboard).</summary>
    Task<List<Shipping>> GetAllAsync(ShippingStatus? status);
}
