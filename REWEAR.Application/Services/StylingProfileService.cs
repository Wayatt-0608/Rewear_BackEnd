using MongoDB.Driver;
using REWEAR.Application.DTOs;
using REWEAR.Application.Interfaces;
using REWEAR.Domain.Entities;
using REWEAR.Domain.Enums;

namespace REWEAR.Application.Services;

/// <summary>
/// Service quản lý hồ sơ sở thích thời trang (Styling Profile) của người dùng.
/// </summary>
/// <remarks>
/// <para><b>Nguyên tắc:</b></para>
/// <list type="bullet">
/// <item>userId LUÔN lấy từ JWT (Controller), không nhận từ request.</item>
/// <item>Mỗi user có tối đa 1 hồ sơ (unique index trên userId).</item>
/// <item>Mọi trường đều tùy chọn; người dùng chỉ cần điền những gì muốn chia sẻ.</item>
/// <item>PUT có ngữ nghĩa ghi đè toàn bộ: nhóm thông tin không gửi lại sẽ bị xóa.</item>
/// <item>Phong cách được chuẩn hóa theo enum <see cref="FashionStyle"/>; kích cỡ và màu
/// giữ dạng chuỗi tự do để khớp trực tiếp với Product.Size / Product.Color.</item>
/// <item>Danh mục yêu thích được kiểm tra tồn tại; response đọc tên danh mục LIVE.</item>
/// <item>Không đụng vào User, Cart, Order, Payment, Shipping, Wishlist.</item>
/// </list>
/// </remarks>
public class StylingProfileService : IStylingProfileService
{
    /// <summary>
    /// Số phong cách tối đa được lưu.
    /// </summary>
    private const int MaxStyles = 10;

    /// <summary>
    /// Số kích cỡ tối đa được lưu.
    /// </summary>
    private const int MaxSizes = 20;

    /// <summary>
    /// Số màu sắc tối đa được lưu.
    /// </summary>
    private const int MaxColors = 20;

    /// <summary>
    /// Số danh mục yêu thích tối đa được lưu.
    /// </summary>
    private const int MaxCategories = 20;

    /// <summary>
    /// Độ dài tối đa của một chuỗi kích cỡ / màu sắc.
    /// </summary>
    private const int MaxTermLength = 50;

    /// <summary>
    /// Độ dài tối đa của ghi chú.
    /// </summary>
    private const int MaxNotesLength = 500;

    /// <summary>
    /// Danh sách TÊN phong cách hợp lệ. So khớp trực tiếp theo tên (không phân biệt
    /// hoa thường) thay vì Enum.TryParse, vì TryParse còn chấp nhận chuỗi số.
    /// </summary>
    private static readonly string[] StyleNames = Enum.GetNames<FashionStyle>();

    private readonly IStylingProfileRepository _stylingProfileRepository;
    private readonly ICategoryRepository _categoryRepository;

    public StylingProfileService(
        IStylingProfileRepository stylingProfileRepository,
        ICategoryRepository categoryRepository)
    {
        _stylingProfileRepository = stylingProfileRepository;
        _categoryRepository = categoryRepository;
    }

    // ============================================
    // LẤY HỒ SƠ
    // ============================================

    /// <summary>
    /// Lấy hồ sơ của user. Trả về null nếu user chưa tạo hồ sơ.
    /// </summary>
    public async Task<StylingProfileResponse?> GetAsync(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId)) return null;

        var profile = await _stylingProfileRepository.GetByUserIdAsync(userId);
        if (profile == null) return null;

        return await MapToResponseAsync(profile);
    }

    // ============================================
    // TẠO / CẬP NHẬT
    // ============================================

    /// <summary>
    /// Tạo mới nếu chưa có hồ sơ, cập nhật nếu đã có.
    /// </summary>
    public async Task<ApiResponse> UpsertAsync(string userId, UpsertStylingProfileRequest request)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return Fail("Không xác định được người dùng.", ApiErrorCode.Forbidden);

        if (request == null)
            return Fail("Nội dung hồ sơ không hợp lệ.");

        // ===== VALIDATE PHONG CÁCH =====
        var styles = new List<string>();
        if (request.Styles != null)
        {
            if (request.Styles.Count > MaxStyles)
                return Fail($"Tối đa {MaxStyles} phong cách.");

            foreach (var raw in request.Styles)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var value = raw.Trim();

                // Chỉ nhận TÊN phong cách. Enum.TryParse còn chấp nhận chuỗi số
                // ("7" -> giá trị ngoài danh sách, "0" -> Minimalist), nên phải so
                // khớp trực tiếp với danh sách tên hợp lệ.
                var canonical = StyleNames.FirstOrDefault(n => string.Equals(n, value, StringComparison.OrdinalIgnoreCase));
                if (canonical == null)
                    return Fail($"Phong cách '{value}' không hợp lệ. Hợp lệ: {string.Join(", ", StyleNames)}.");

                if (!styles.Contains(canonical)) styles.Add(canonical);
            }
        }

        // ===== VALIDATE KÍCH CỚ =====
        var sizes = ValidateTerms(request.Sizes, MaxSizes, MaxTermLength, "kích cỡ", out var sizesError);
        if (sizes == null)
            return Fail(sizesError!);

        // ===== VALIDATE MÀU SẮC =====
        var colors = ValidateTerms(request.Colors, MaxColors, MaxTermLength, "màu sắc", out var colorsError);
        if (colors == null)
            return Fail(colorsError!);

        // ===== VALIDATE DANH MỤC YÊU THÍCH =====
        var categoryIds = new List<string>();
        if (request.FavoriteCategoryIds != null)
        {
            if (request.FavoriteCategoryIds.Count > MaxCategories)
                return Fail($"Tối đa {MaxCategories} danh mục yêu thích.");

            var requestedIds = new List<string>();
            foreach (var raw in request.FavoriteCategoryIds)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var id = raw.Trim();
                if (!MongoDB.Bson.ObjectId.TryParse(id, out _))
                    return Fail($"Id danh mục '{id}' không hợp lệ.");
                if (!requestedIds.Contains(id)) requestedIds.Add(id);
            }

            // Batch lookup: 1 query thay vì N+1 query theo từng danh mục.
            var categories = await _categoryRepository.GetByIdsAsync(requestedIds);
            var categoryMap = categories.ToDictionary(c => c.Id);

            foreach (var id in requestedIds)
            {
                if (!categoryMap.TryGetValue(id, out var category))
                    return Fail($"Không tìm thấy danh mục '{id}'.", ApiErrorCode.NotFound);

                // Danh mục đã bị ẩn (soft delete) thì không nên là yêu thích sản phẩm
                // của nó, và cũng không khớp với cách ProductService từ chối danh mục
                // không hoạt động.
                if (!category.IsActive)
                    return Fail($"Danh mục '{category.Name}' hiện không hoạt động.", ApiErrorCode.Validation);

                categoryIds.Add(id);
            }
        }

        // ===== VALIDATE NGÂN SÁCH =====
        if (request.MinBudget.HasValue && request.MinBudget.Value < 0)
            return Fail("Ngân sách tối thiểu không được âm.");
        if (request.MaxBudget.HasValue && request.MaxBudget.Value < 0)
            return Fail("Ngân sách tối đa không được âm.");
        if (request.MinBudget.HasValue && request.MaxBudget.HasValue
            && request.MinBudget.Value > request.MaxBudget.Value)
            return Fail("Ngân sách tối thiểu không được lớn hơn ngân sách tối đa.");

        // ===== VALIDATE GHI CHÚ =====
        string? notes = null;
        if (!string.IsNullOrWhiteSpace(request.Notes))
        {
            notes = request.Notes.Trim();
            if (notes.Length > MaxNotesLength)
                return Fail($"Ghi chú tối đa {MaxNotesLength} ký tự.");
        }

        var existing = await _stylingProfileRepository.GetByUserIdAsync(userId);

        if (existing == null)
        {
            var profile = new StylingProfile
            {
                UserId = userId,
                Styles = styles,
                Sizes = sizes,
                Colors = colors,
                FavoriteCategoryIds = categoryIds,
                MinBudget = request.MinBudget,
                MaxBudget = request.MaxBudget,
                Notes = notes,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            try
            {
                await _stylingProfileRepository.CreateAsync(profile);
            }
            catch (MongoWriteException ex) when (ex.WriteError?.Code == 11000)
            {
                // Request chạy song song đã tạo hồ sơ trước -> đọc lại và cập nhật,
                // để vẫn giữ được tính "1 user = 1 hồ sơ" mà không lỗi.
                var concurrent = await _stylingProfileRepository.GetByUserIdAsync(userId);
                if (concurrent == null)
                    return Fail("Không tạo được hồ sơ. Vui lòng thử lại.", ApiErrorCode.Conflict);

                ApplyChanges(concurrent, styles, sizes, colors, categoryIds, request.MinBudget, request.MaxBudget, notes);
                if (!await _stylingProfileRepository.UpdateAsync(concurrent))
                    return Fail("Hồ sơ vừa bị thay đổi. Vui lòng thử lại.", ApiErrorCode.Conflict);

                return Success("Đã cập nhật hồ sơ sở thích thời trang.", await MapToResponseAsync(concurrent));
            }

            return Success("Đã tạo hồ sơ sở thích thời trang.", await MapToResponseAsync(profile));
        }

        ApplyChanges(existing, styles, sizes, colors, categoryIds, request.MinBudget, request.MaxBudget, notes);

        // Bản ghi có thể đã bị xóa bởi một request DELETE chạy song song -> không
        // được âm thầm trả thành công khi thực tế chưa lưu được gì.
        if (!await _stylingProfileRepository.UpdateAsync(existing))
            return Fail("Hồ sơ vừa bị thay đổi. Vui lòng thử lại.", ApiErrorCode.Conflict);

        return Success("Đã cập nhật hồ sơ sở thích thời trang.", await MapToResponseAsync(existing));
    }

    // ============================================
    // XÓA HỒ SƠ
    // ============================================

    /// <summary>
    /// Xóa hồ sơ. Idempotent: chưa có hồ sơ vẫn trả thành công.
    /// </summary>
    public async Task<ApiResponse> DeleteAsync(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return Fail("Không xác định được người dùng.", ApiErrorCode.Forbidden);

        var deleted = await _stylingProfileRepository.DeleteByUserIdAsync(userId);

        return Success(deleted
            ? "Đã xóa hồ sơ sở thích thời trang."
            : "Chưa có hồ sơ sở thích thời trang để xóa.");
    }

    // ============================================
    // HELPERS
    // ============================================

    /// <summary>
    /// Ghi đè nội dung hồ sơ (PUT = replace).
    /// </summary>
    private static void ApplyChanges(
        StylingProfile profile,
        List<string> styles,
        List<string> sizes,
        List<string> colors,
        List<string> categoryIds,
        decimal? minBudget,
        decimal? maxBudget,
        string? notes)
    {
        profile.Styles = styles;
        profile.Sizes = sizes;
        profile.Colors = colors;
        profile.FavoriteCategoryIds = categoryIds;
        profile.MinBudget = minBudget;
        profile.MaxBudget = maxBudget;
        profile.Notes = notes;
        profile.UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Chuẩn hóa danh sách chuỗi: trim, bỏ rỗng, bỏ trùng, giữ nguyên thứ tự.
    /// </summary>
    /// <param name="values">Danh sách đầu vào (có thể null).</param>
    /// <param name="maxCount">Tối đa bao nhiêu mục.</param>
    /// <param name="maxLength">Tối đa bao nhiêu ký tự mỗi mục.</param>
    /// <param name="label">Tên hiển thị của trường (kích cỡ / màu sắc).</param>
    /// <param name="error">Thông báo lỗi nếu có; null nếu dữ liệu hợp lệ.</param>
    /// <returns>Danh sách đã chuẩn hóa, hoặc null nếu dữ liệu không hợp lệ.</returns>
    private static List<string>? ValidateTerms(
        List<string>? values, int maxCount, int maxLength, string label, out string? error)
    {
        error = null;

        if (values == null) return new List<string>();

        if (values.Count > maxCount)
        {
            error = $"Danh sách {label} không được vượt quá {maxCount} mục.";
            return null;
        }

        var result = new List<string>();
        foreach (var raw in values)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var value = raw.Trim();
            if (value.Length > maxLength)
            {
                error = $"Mỗi {label} tối đa {maxLength} ký tự.";
                return null;
            }
            if (!result.Contains(value)) result.Add(value);
        }

        return result;
    }    /// <summary>
    /// Map entity sang DTO response, đọc tên danh mục LIVE từ collection Categories.
    /// </summary>
    private async Task<StylingProfileResponse> MapToResponseAsync(StylingProfile profile)
    {
        var categories = new List<StylingProfileCategoryResponse>();

        // Batch lookup: 1 query thay vì N+1 query theo từng danh mục.
        var categoryMap = (await _categoryRepository.GetByIdsAsync(profile.FavoriteCategoryIds))
            .ToDictionary(c => c.Id);

        // Giữ đúng thứ tự danh mục mà người dùng đã lưu.
        foreach (var id in profile.FavoriteCategoryIds)
        {
            // Danh mục đã bị xóa khỏi catalog -> bỏ qua, không làm hỏng response.
            if (!categoryMap.TryGetValue(id, out var category)) continue;

            categories.Add(new StylingProfileCategoryResponse
            {
                Id = category.Id,
                Name = category.Name,
                Slug = category.Slug
            });
        }

        return new StylingProfileResponse
        {
            Id = profile.Id,
            Styles = profile.Styles.ToList(),
            Sizes = profile.Sizes.ToList(),
            Colors = profile.Colors.ToList(),
            FavoriteCategoryIds = profile.FavoriteCategoryIds.ToList(),
            FavoriteCategories = categories,
            MinBudget = profile.MinBudget,
            MaxBudget = profile.MaxBudget,
            Notes = profile.Notes,
            CreatedAt = profile.CreatedAt,
            UpdatedAt = profile.UpdatedAt
        };
    }

    private static ApiResponse Success(string message, object? data = null)
        => new ApiResponse { Success = true, Message = message, Data = data };

    private static ApiResponse Fail(string message, string errorCode = ApiErrorCode.Validation)
        => new ApiResponse { Success = false, Message = message, ErrorCode = errorCode };
}
