namespace REWEAR.Domain.Entities;

/// <summary>
/// Các vai trò (role) trong hệ thống.
/// </summary>
public enum UserRole
{
    /// <summary>
    /// Thành viên thông thường (mặc định)
    /// </summary>
    Member = 0,

    /// <summary>
    /// Quản trị viên hệ thống
    /// </summary>
    Admin = 1,

    /// <summary>
    /// Nhân viên (staff)
    /// </summary>
    Staff = 2,

    /// <summary>
    /// Người giao hàng
    /// </summary>
    Shipper = 3
}
