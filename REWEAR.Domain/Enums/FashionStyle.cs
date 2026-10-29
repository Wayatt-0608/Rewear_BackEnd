namespace REWEAR.Domain.Enums;

/// <summary>
/// Phong cách thời trang mà người dùng có thể khai báo trong Styling Profile.
/// </summary>
/// <remarks>
/// <para>
/// Hệ thống chưa có bảng/enum phong cách sẵn (catalog chỉ có Category và
/// Brand), nên enum này là nguồn danh sách phong cách DUY NHẤT của tính năng
/// Styling Profile. Giá trị được lưu dưới dạng chuỗi trong MongoDB nên đổi thứ tự
/// hoặc thêm giá trị mới không làm hỏng dữ liệu cũ.
/// </para>
/// <para>
/// Enum này KHÔNG dùng để gợi ý sản phẩm (Recommendation/AI Styling chưa triển
/// khai trong phạm vi hiện tại), chỉ dùng để chuẩn hóa và kiểm tra dữ liệu đầu vào.
/// </para>
/// </remarks>
public enum FashionStyle
{
    /// <summary>
    /// Tối giản: màu trung tính, form đơn giản, ít chi tiết.
    /// </summary>
    Minimalist = 0,

    /// <summary>
    /// Casual: đồ mặc thường ngày, thoải mái.
    /// </summary>
    Casual = 1,

    /// <summary>
    /// Streetwear: phong cách đường phố, form rộng, logo/graphic.
    /// </summary>
    Streetwear = 2,

    /// <summary>
    /// Vintage: đồ cũ, hoặc kiểu retro.
    /// </summary>
    Vintage = 3,

    /// <summary>
    /// Thể thao: đồ activewear, gym.
    /// </summary>
    Sporty = 4,

    /// <summary>
    /// Cổ điển: form truyền thống, thanh lịch.
    /// </summary>
    Classic = 5,

    /// <summary>
    /// Thời trang cao cấp, tinh tế.
    /// </summary>
    Elegant = 6
}
