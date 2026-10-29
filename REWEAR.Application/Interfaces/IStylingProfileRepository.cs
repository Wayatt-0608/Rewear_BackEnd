using REWEAR.Domain.Entities;

namespace REWEAR.Application.Interfaces;

/// <summary>
/// Repository cho StylingProfile - lưu hồ sơ sở thích thời trang của người dùng.
/// </summary>
/// <remarks>
/// Mỗi user có tối đa 1 hồ sơ. Ràng buộc này được MongoDB đảm bảo bằng unique
/// index trên <c>userId</c> của collection "StylingProfiles", nên 2 request lưu
/// chạy song song vẫn chỉ tạo được 1 bản ghi (request thua nhận
/// MongoWriteException code 11000 và được service xử lý).
/// </remarks>
public interface IStylingProfileRepository
{
    /// <summary>
    /// Lấy hồ sơ theo userId. Trả về null nếu user chưa có hồ sơ.
    /// </summary>
    Task<StylingProfile?> GetByUserIdAsync(string userId);

    /// <summary>
    /// Tạo hồ sơ mới.
    /// </summary>
    /// <remarks>
    /// Ném <c>MongoWriteException</c> với WriteError.Code = 11000 nếu user đã có hồ sơ
    /// do request chạy song song.
    /// </remarks>
    Task CreateAsync(StylingProfile profile);

    /// <summary>
    /// Cập nhật hồ sơ (ghi đè toàn bộ nội dung).
    /// </summary>
    /// <returns>
    /// True nếu đã ghi được vào một bản ghi; false nếu bản ghi không còn tồn tại
    /// (ví dụ bị xóa bởi một request DELETE chạy song song). Khi false, service
    /// phải báo conflict thay vì âm thầm trả thành công.
    /// </returns>
    Task<bool> UpdateAsync(StylingProfile profile);

    /// <summary>
    /// Xóa hồ sơ theo userId.
    /// </summary>
    /// <returns>True nếu có bản ghi bị xóa, false nếu không tồn tại (idempotent).</returns>
    Task<bool> DeleteByUserIdAsync(string userId);
}
