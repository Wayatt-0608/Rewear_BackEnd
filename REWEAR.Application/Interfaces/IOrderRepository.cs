using REWEAR.Domain.Entities;
using REWEAR.Domain.Enums;

namespace REWEAR.Application.Interfaces;

/// <summary>
/// Interface cho Order Repository.
/// </summary>
public interface IOrderRepository
{
    /// <summary>
    /// Lấy đơn hàng theo Id kỹ thuật.
    /// </summary>
    Task<Order?> GetByIdAsync(string id);

    /// <summary>
    /// Lấy đơn hàng theo mã hiển thị cho người dùng (vd: RW-20261005-A1B2C3).
    /// </summary>
    Task<Order?> GetByOrderCodeAsync(string orderCode);

    /// <summary>
    /// Tạo đơn hàng mới.
    /// </summary>
    Task CreateAsync(Order order);

    /// <summary>
    /// Cập nhật đơn hàng (ghi đè toàn bộ document).
    /// </summary>
    Task UpdateAsync(Order order);

    /// <summary>
    /// Lấy danh sách đơn hàng của một người dùng, mới nhất trước.
    /// </summary>
    Task<List<Order>> GetByUserIdAsync(string userId, OrderStatus? status);

    /// <summary>
    /// Lấy danh sách đơn hàng phân trang cho một người dùng.
    /// </summary>
    /// <returns>Danh sách đơn + tổng số bản ghi khớp điều kiện.</returns>
    Task<(List<Order> Orders, long TotalCount)> GetPagedByUserIdAsync(
        string userId, OrderStatus? status, int page, int pageSize);

    /// <summary>
    /// Đếm số đơn theo trạng thái (dùng cho dashboard Task 18).
    /// </summary>
    Task<long> CountByStatusAsync(OrderStatus status);
}