using REWEAR.Domain.Entities;
using REWEAR.Domain.Enums;

namespace REWEAR.Application.Interfaces;

/// <summary>
/// Repository cho Shipping (Task 9).
/// </summary>
public interface IShippingRepository
{
    Task CreateAsync(Shipping shipping);

    Task UpdateAsync(Shipping shipping);

    /// <summary>Lấy thông tin vận chuyển của một đơn (mỗi đơn chỉ có 1 bản ghi).</summary>
    Task<Shipping?> GetByOrderIdAsync(string orderId);

    /// <summary>Lấy danh sách vận chuyển theo trạng thái (dashboard shipper).</summary>
    Task<List<Shipping>> GetByStatusAsync(ShippingStatus status);
}