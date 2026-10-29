using REWEAR.Application.DTOs;

namespace REWEAR.Application.Interfaces;

/// <summary>
/// Service xử lý hồ sơ sở thích thời trang (Styling Profile) của người dùng.
/// </summary>
/// <remarks>
/// Mọi thao tác đều dựa trên userId lấy từ JWT, KHÔNG nhận userId từ request,
/// nên người dùng chỉ thao tác được trên hồ sơ của chính mình.
/// </remarks>
public interface IStylingProfileService
{
    /// <summary>
    /// Lấy hồ sơ của người dùng. Trả về null nếu chưa có hồ sơ.
    /// </summary>
    Task<StylingProfileResponse?> GetAsync(string userId);

    /// <summary>
    /// Tạo mới nếu chưa có, cập nhật nếu đã có. Mỗi user chỉ có tối đa 1 hồ sơ.
    /// </summary>
    Task<ApiResponse> UpsertAsync(string userId, UpsertStylingProfileRequest request);

    /// <summary>
    /// Xóa hồ sơ. Idempotent: chưa có hồ sơ vẫn trả thành công.
    /// </summary>
    Task<ApiResponse> DeleteAsync(string userId);
}
