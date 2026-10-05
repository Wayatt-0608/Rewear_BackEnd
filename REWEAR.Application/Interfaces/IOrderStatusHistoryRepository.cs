using REWEAR.Domain.Entities;

namespace REWEAR.Application.Interfaces;

/// <summary>
/// Repository cho timeline trạng thái đơn (Task 7).
/// </summary>
public interface IOrderStatusHistoryRepository
{
    /// <summary>
    /// Ghi 1 mốc thay đổi trạng thái. Append-only, không sửa document cũ.
    /// </summary>
    Task CreateAsync(OrderStatusHistory history);

    /// <summary>
    /// Lấy toàn bộ timeline của đơn, cũ nhất trước (để hiển thị theo dòng thời gian).
    /// </summary>
    Task<List<OrderStatusHistory>> GetByOrderIdAsync(string orderId);
}