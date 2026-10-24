using System.ComponentModel.DataAnnotations;
using REWEAR.Domain.Enums;

namespace REWEAR.Application.DTOs;

// ============================================
// VOUCHER DTOs (Task 10)
// ============================================

/// <summary>
/// Request tạo voucher mới (Admin).
/// </summary>
public class CreateVoucherRequest
{
    /// <summary>
    /// Mã voucher (VD: "TET2026"). Bắt buộc, viết HOA, không dấu.
    /// </summary>
    [Required]
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Tên hiển thị (VD: "Tết 2026 - Giảm 15% đơn từ 400k"). Bắt buộc.
    /// </summary>
    [Required]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Mô tả chi tiết (tùy chọn).
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Loại giảm giá: Percentage (0) hoặc FixedAmount (1).
    /// </summary>
    public VoucherType Type { get; set; } = VoucherType.Percentage;

    /// <summary>
    /// Giá trị giảm. Percentage: 1-100 (%). FixedAmount: số tiền cố định (VNĐ).
    /// </summary>
    public decimal Value { get; set; }

    /// <summary>
    /// Tổng đơn tối thiểu mới được dùng. Mặc định 0 (không yêu cầu).
    /// </summary>
    public decimal MinOrderAmount { get; set; } = 0m;

    /// <summary>
    /// Số tiền giảm tối thiểu (áp dụng cho cả 2 loại). Mặc định 0.
    /// </summary>
    public decimal MinDiscount { get; set; } = 0m;

    /// <summary>
    /// Số tiền giảm tối đa (chủ yếu cho Percentage). Mặc định 0 (không giới hạn).
    /// </summary>
    public decimal MaxDiscount { get; set; } = 0m;

    /// <summary>
    /// Ngày bắt đầu áp dụng. Mặc định = thời điểm tạo.
    /// </summary>
    public DateTime? StartDate { get; set; }

    /// <summary>
    /// Ngày hết hạn. Bắt buộc phải sau StartDate.
    /// </summary>
    [Required]
    public DateTime ExpiryDate { get; set; }

    /// <summary>
    /// Tổng lượt dùng trên toàn hệ thống. 0 = không giới hạn. Mặc định 0.
    /// </summary>
    public int UsageLimit { get; set; } = 0;

    /// <summary>
    /// Mỗi user được dùng tối đa bao nhiêu lần. Mặc định 1.
    /// </summary>
    public int UsageLimitPerUser { get; set; } = 1;
}

/// <summary>
/// Request cập nhật voucher (Admin). Cho phép sửa mọi trường trừ Code và UsedCount.
/// </summary>
public class UpdateVoucherRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public VoucherType Type { get; set; }
    public decimal Value { get; set; }
    public decimal MinOrderAmount { get; set; } = 0m;
    public decimal MinDiscount { get; set; } = 0m;
    public decimal MaxDiscount { get; set; } = 0m;
    public DateTime StartDate { get; set; }
    public DateTime ExpiryDate { get; set; }
    public int UsageLimit { get; set; } = 0;
    public int UsageLimitPerUser { get; set; } = 1;

    /// <summary>
    /// Admin bật/tắt voucher. Khi tắt, user không dùng được nữa (nhưng vẫn hiển thị
    /// trong kho như đã "hết hạn" / "tạm dừng").
    /// </summary>
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Phản hồi thông tin voucher cho Admin.
/// </summary>
public class VoucherResponse
{
    public string Id { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Type { get; set; } = string.Empty;
    public decimal Value { get; set; }
    public decimal MinOrderAmount { get; set; }
    public decimal MinDiscount { get; set; }
    public decimal MaxDiscount { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime ExpiryDate { get; set; }
    public int UsageLimit { get; set; }
    public int UsedCount { get; set; }

    /// <summary>Số lượt đang được giữ chỗ cho các đơn chờ thanh toán (chưa dùng thật).</summary>
    public int ReservedCount { get; set; }

    public int UsageLimitPerUser { get; set; }
    public bool IsActive { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // ====== Thông tin phái sinh (tiện cho FE) ======
    /// <summary>True nếu đang trong thời hạn hiệu lực (StartDate ≤ now ≤ ExpiryDate).</summary>
    public bool IsWithinDateRange { get; set; }

    /// <summary>True nếu còn lượt dùng (UsedCount &lt; UsageLimit hoặc không giới hạn).</summary>
    public bool HasRemainingUsage { get; set; }

    /// <summary>Trạng thái hiển thị: "active" / "expired" / "exhausted" / "inactive".</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Số lượt còn lại (-1 = không giới hạn).</summary>
    public int RemainingUsage { get; set; }
}

/// <summary>
/// Phản hồi voucher trong kho của user (FE "Kho voucher").
/// </summary>
public class UserVoucherResponse
{
    public string Id { get; set; } = string.Empty;
    public string VoucherId { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Type { get; set; } = string.Empty;
    public decimal Value { get; set; }
    public decimal MinOrderAmount { get; set; }
    public decimal MinDiscount { get; set; }
    public decimal MaxDiscount { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime ExpiryDate { get; set; }
    public bool IsUsed { get; set; }
    public DateTime? UsedAt { get; set; }

    /// <summary>Thời điểm voucher được giữ chỗ cho một đơn chờ thanh toán (null = không giữ chỗ).</summary>
    public DateTime? ReservedAt { get; set; }

    /// <summary>Số lần user đã dùng thật voucher này.</summary>
    public int UsageCount { get; set; }

    /// <summary>Số lần user được phép dùng voucher này (0 = không giới hạn).</summary>
    public int UsageLimitPerUser { get; set; }

    /// <summary>Id đơn hàng đang giữ chỗ hoặc đã dùng voucher (null = chưa gắn đơn nào).</summary>
    public string? OrderId { get; set; }
    public DateTime GrantedAt { get; set; }

    // Phái sinh
    /// <summary>
    /// Trạng thái hiển thị: "available" / "reserved" / "used" / "expired" / "upcoming" / "exhausted".
    /// </summary>
    public string Status { get; set; } = string.Empty;
    /// <summary>True nếu user có thể dùng ngay (còn hạn + chưa giữ chỗ + voucher active + còn lượt).</summary>
    public bool IsUsable { get; set; }
}

// ============================================
// APPLY VOUCHER (User)
// ============================================

/// <summary>
/// Request áp dụng voucher vào đơn hàng. User nhập mã + tổng đơn để xem trước số tiền giảm.
/// </summary>
public class ApplyVoucherRequest
{
    /// <summary>
    /// Mã voucher (VD: "TET2026"). Tìm chính xác theo Code (case-insensitive).
    /// </summary>
    [Required]
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Tổng tiền đơn hàng (SubTotal) trước khi áp voucher.
    /// </summary>
    public decimal OrderTotal { get; set; }
}

/// <summary>
/// Phản hồi preview khi áp voucher thành công.
/// </summary>
public class ApplyVoucherResponse
{
    public string VoucherId { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string DiscountType { get; set; } = string.Empty;
    public decimal DiscountValue { get; set; }
    public decimal MinOrderAmount { get; set; }
    public decimal MinDiscount { get; set; }
    public decimal MaxDiscount { get; set; }

    /// <summary>Số tiền giảm tính thô từ % hoặc fixed.</summary>
    public decimal RawDiscount { get; set; }

    /// <summary>Số tiền giảm thực tế sau khi áp Min/Max (final).</summary>
    public decimal FinalDiscount { get; set; }

    public decimal OriginalTotal { get; set; }
    public decimal FinalTotal { get; set; }
    public string Message { get; set; } = string.Empty;
}

// ============================================
// GRANT VOUCHER (Admin)
// ============================================

/// <summary>
/// Request phát voucher cho 1 user cụ thể (Admin).
/// </summary>
public class GrantVoucherToUserRequest
{
    /// <summary>
    /// Id user được phát. Nếu user không phải Member sẽ trả lỗi.
    /// </summary>
    [Required]
    public string UserId { get; set; } = string.Empty;
}

// ============================================
// VOUCHER STATS (Admin Dashboard)
// ============================================

/// <summary>
/// Thống kê sử dụng voucher (Admin).
/// </summary>
public class VoucherStatsResponse
{
    public string VoucherId { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int TotalGranted { get; set; }
    public int TotalUsed { get; set; }

    /// <summary>Số lượt đang giữ chỗ cho đơn chờ thanh toán (chưa bị tiêu thụ).</summary>
    public int TotalReserved { get; set; }

    public int TotalAvailable { get; set; }
    public int UsageLimit { get; set; }
    public int UsedCount { get; set; }
    public decimal TotalDiscountAmount { get; set; }
}
