using MongoDB.Bson;
using REWEAR.Application.DTOs;
using REWEAR.Application.Interfaces;
using REWEAR.Domain.Entities;
using REWEAR.Domain.Enums;

namespace REWEAR.Application.Services;

/// <summary>
/// Service xử lý business logic cho Voucher (Task 10).
/// </summary>
/// <remarks>
/// Quy tắc quan trọng:
/// 1. Code unique trong hệ thống (case-insensitive).
/// 2. Voucher chỉ dùng được khi: IsActive + trong thời hạn + còn lượt.
/// 3. Tính discount 3 bước: raw → clamp max → clamp min.
/// 4. Phát voucher cho Member mặc định (bỏ Admin/Staff/Shipper).
/// </remarks>
public class VoucherService : IVoucherService
{
    private readonly IVoucherRepository _voucherRepository;
    private readonly IUserVoucherRepository _userVoucherRepository;
    private readonly IUserRepository _userRepository;

    public VoucherService(
        IVoucherRepository voucherRepository,
        IUserVoucherRepository userVoucherRepository,
        IUserRepository userRepository)
    {
        _voucherRepository = voucherRepository;
        _userVoucherRepository = userVoucherRepository;
        _userRepository = userRepository;
    }

    // ============================================
    // CRUD (Admin)
    // ============================================

    public async Task<VoucherResponse> CreateAsync(CreateVoucherRequest request, string adminId)
    {
        // ===== VALIDATE INPUT =====
        if (string.IsNullOrWhiteSpace(request.Code))
            throw new ArgumentException("Mã voucher không được để trống.");

        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ArgumentException("Tên voucher không được để trống.");

        // Chuẩn hóa mã: trim + uppercase
        string code = request.Code.Trim().ToUpperInvariant();

        if (code.Length < 3 || code.Length > 32)
            throw new ArgumentException("Mã voucher phải có từ 3 đến 32 ký tự.");

        // Mã chỉ chứa chữ cái, số, dấu gạch ngang
        if (!System.Text.RegularExpressions.Regex.IsMatch(code, @"^[A-Z0-9\-_]+$"))
            throw new ArgumentException("Mã voucher chỉ chứa chữ HOA, số, gạch ngang hoặc gạch dưới.");

        // Validate value theo Type
        if (request.Type == VoucherType.Percentage)
        {
            if (request.Value <= 0 || request.Value > 100)
                throw new ArgumentException("Voucher Percentage phải có Value từ 1 đến 100 (%).");
        }
        else
        {
            if (request.Value <= 0)
                throw new ArgumentException("Voucher FixedAmount phải có Value lớn hơn 0.");
        }

        // Validate dates
        DateTime startDate = request.StartDate ?? DateTime.UtcNow;
        if (request.ExpiryDate <= startDate)
            throw new ArgumentException("Ngày hết hạn phải sau ngày bắt đầu.");

        if (request.ExpiryDate <= DateTime.UtcNow)
            throw new ArgumentException("Ngày hết hạn phải ở tương lai.");

        // Validate Min/Max discount
        if (request.MinDiscount < 0)
            throw new ArgumentException("MinDiscount không được âm.");

        if (request.MaxDiscount < 0)
            throw new ArgumentException("MaxDiscount không được âm.");

        if (request.MaxDiscount > 0 && request.MinDiscount > request.MaxDiscount)
            throw new ArgumentException("MinDiscount không được lớn hơn MaxDiscount.");

        // Validate UsageLimit
        if (request.UsageLimit < 0)
            throw new ArgumentException("UsageLimit không được âm.");

        if (request.UsageLimitPerUser < 0)
            throw new ArgumentException("UsageLimitPerUser không được âm.");

        // Check trùng code
        var existing = await _voucherRepository.GetByCodeAsync(code);
        if (existing != null)
            throw new InvalidOperationException($"Mã voucher '{code}' đã tồn tại.");

        // ===== TẠO VOUCHER =====
        var voucher = new Voucher
        {
            Code = code,
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            Type = request.Type,
            Value = request.Value,
            MinOrderAmount = request.MinOrderAmount,
            MinDiscount = request.MinDiscount,
            MaxDiscount = request.MaxDiscount,
            StartDate = startDate,
            ExpiryDate = request.ExpiryDate,
            UsageLimit = request.UsageLimit,
            UsageLimitPerUser = request.UsageLimitPerUser,
            UsedCount = 0,
            ReservedCount = 0,
            IsActive = true,
            CreatedBy = adminId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _voucherRepository.CreateAsync(voucher);

        return MapToResponse(voucher);
    }

    public async Task<VoucherResponse?> UpdateAsync(string id, UpdateVoucherRequest request)
    {
        if (!ObjectId.TryParse(id, out _))
            return null;

        var voucher = await _voucherRepository.GetByIdAsync(id);
        if (voucher == null)
            return null;

        // ===== VALIDATE =====
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ArgumentException("Tên voucher không được để trống.");

        if (request.Type == VoucherType.Percentage)
        {
            if (request.Value <= 0 || request.Value > 100)
                throw new ArgumentException("Voucher Percentage phải có Value từ 1 đến 100 (%).");
        }
        else
        {
            if (request.Value <= 0)
                throw new ArgumentException("Voucher FixedAmount phải có Value lớn hơn 0.");
        }

        if (request.ExpiryDate <= request.StartDate)
            throw new ArgumentException("Ngày hết hạn phải sau ngày bắt đầu.");

        if (request.MaxDiscount > 0 && request.MinDiscount > request.MaxDiscount)
            throw new ArgumentException("MinDiscount không được lớn hơn MaxDiscount.");

        if (request.UsageLimit < 0 || request.UsageLimitPerUser < 0)
            throw new ArgumentException("UsageLimit không được âm.");

        // Không cho phép giảm UsageLimit xuống dưới số lượt đã chiếm
        // (đã dùng thật + đang giữ chỗ). Nếu không, các lượt đang giữ chỗ sẽ
        // vượt quá giới hạn mới.
        int claimedCount = voucher.UsedCount + voucher.ReservedCount;
        if (request.UsageLimit > 0 && claimedCount > request.UsageLimit)
            throw new InvalidOperationException(
                $"Không thể giảm UsageLimit xuống {request.UsageLimit} vì đang có {claimedCount} lượt đã dùng/giữ chỗ.");

        // ===== UPDATE =====
        voucher.Name = request.Name.Trim();
        voucher.Description = request.Description?.Trim();
        voucher.Type = request.Type;
        voucher.Value = request.Value;
        voucher.MinOrderAmount = request.MinOrderAmount;
        voucher.MinDiscount = request.MinDiscount;
        voucher.MaxDiscount = request.MaxDiscount;
        voucher.StartDate = request.StartDate;
        voucher.ExpiryDate = request.ExpiryDate;
        voucher.UsageLimit = request.UsageLimit;
        voucher.UsageLimitPerUser = request.UsageLimitPerUser;
        voucher.IsActive = request.IsActive;
        voucher.UpdatedAt = DateTime.UtcNow;

        await _voucherRepository.UpdateAsync(voucher);

        return MapToResponse(voucher);
    }

    public async Task<bool> DeleteAsync(string id)
    {
        if (!ObjectId.TryParse(id, out _))
            return false;

        var voucher = await _voucherRepository.GetByIdAsync(id);
        if (voucher == null)
            return false;

        // Soft delete: tắt active. Không xóa cứng vì UserVoucher còn tham chiếu.
        voucher.IsActive = false;
        voucher.UpdatedAt = DateTime.UtcNow;

        await _voucherRepository.UpdateAsync(voucher);
        return true;
    }

    public async Task<VoucherResponse?> GetByIdAsync(string id)
    {
        if (!ObjectId.TryParse(id, out _))
            return null;

        var voucher = await _voucherRepository.GetByIdAsync(id);
        return voucher != null ? MapToResponse(voucher) : null;
    }

    public async Task<List<VoucherResponse>> GetAllAsync(bool? isActive = null)
    {
        var vouchers = await _voucherRepository.GetAllAsync(isActive);
        return vouchers.Select(MapToResponse).ToList();
    }

    // ============================================
    // GRANT (Admin)
    // ============================================

    public async Task<GrantAllVouchersResult> GrantToAllMembersAsync(string voucherId, string adminId)
    {
        if (!ObjectId.TryParse(voucherId, out _))
            throw new ArgumentException("VoucherId không hợp lệ.");

        var voucher = await _voucherRepository.GetByIdAsync(voucherId);
        if (voucher == null)
            throw new ArgumentException("Không tìm thấy voucher.");

        if (!voucher.IsActive)
            throw new InvalidOperationException("Voucher đang bị tắt, không thể phát.");

        // Lấy tất cả user có Role = Member (bỏ Admin/Staff/Shipper)
        var allUsers = await _userRepository.GetAllAsync();
        var members = allUsers.Where(u => u.Role == UserRole.Member).ToList();

        if (members.Count == 0)
        {
            return new GrantAllVouchersResult
            {
                GrantedCount = 0,
                SkippedCount = 0,
                Message = "Hệ thống chưa có Member nào."
            };
        }

        // Lấy các UserVoucher hiện có của voucher này
        var existingUserVouchers = await _userVoucherRepository.GetByVoucherIdAsync(voucherId);
        var grantedUserIds = existingUserVouchers.Select(uv => uv.UserId).ToHashSet();

        var toInsert = new List<UserVoucher>();
        int skipped = 0;

        foreach (var member in members)
        {
            // Đã phát cho user này rồi → bỏ qua
            if (grantedUserIds.Contains(member.Id))
            {
                skipped++;
                continue;
            }

            toInsert.Add(new UserVoucher
            {
                UserId = member.Id,
                VoucherId = voucherId,
                Status = UserVoucherStatus.Available,
                UsageCount = 0,
                GrantedAt = DateTime.UtcNow,
                GrantedBy = adminId
            });
        }

        if (toInsert.Count > 0)
        {
            await _userVoucherRepository.CreateManyAsync(toInsert);
        }

        return new GrantAllVouchersResult
        {
            GrantedCount = toInsert.Count,
            SkippedCount = skipped,
            Message = $"Đã phát voucher '{voucher.Code}' cho {toInsert.Count} Member. " +
                      (skipped > 0 ? $"Bỏ qua {skipped} user đã nhận trước đó." : "")
        };
    }

    public async Task<bool> GrantToUserAsync(string voucherId, string userId, string adminId)
    {
        if (!ObjectId.TryParse(voucherId, out _))
            throw new ArgumentException("VoucherId không hợp lệ.");

        if (!ObjectId.TryParse(userId, out _))
            throw new ArgumentException("UserId không hợp lệ.");

        var voucher = await _voucherRepository.GetByIdAsync(voucherId);
        if (voucher == null)
            throw new ArgumentException("Không tìm thấy voucher.");

        if (!voucher.IsActive)
            throw new InvalidOperationException("Voucher đang bị tắt, không thể phát.");

        var user = await _userRepository.GetByIdAsync(userId);
        if (user == null)
            throw new ArgumentException("Không tìm thấy user.");

        if (user.Role != UserRole.Member)
            throw new InvalidOperationException(
                $"Chỉ được phát voucher cho Member. User này có role: {user.Role}.");

        // Check đã phát chưa
        var existing = await _userVoucherRepository.GetByUserAndVoucherAsync(userId, voucherId);
        if (existing != null)
            return false; // đã có rồi, idempotent

        var userVoucher = new UserVoucher
        {
            UserId = userId,
            VoucherId = voucherId,
            Status = UserVoucherStatus.Available,
            UsageCount = 0,
            GrantedAt = DateTime.UtcNow,
            GrantedBy = adminId
        };

        await _userVoucherRepository.CreateAsync(userVoucher);
        return true;
    }

    // ============================================
    // STATS (Admin)
    // ============================================

    public async Task<VoucherStatsResponse?> GetStatsAsync(string voucherId)
    {
        if (!ObjectId.TryParse(voucherId, out _))
            return null;

        var voucher = await _voucherRepository.GetByIdAsync(voucherId);
        if (voucher == null)
            return null;

        int totalGranted = await _userVoucherRepository.CountByVoucherAsync(voucherId);
        int totalUsed = await _userVoucherRepository.CountUsedByVoucherAsync(voucherId);
        int totalReserved = await _userVoucherRepository.CountReservedByVoucherAsync(voucherId);

        // Lượt còn khả dụng = đã cấp - đã dùng thật - đang giữ chỗ.
        // Lượt đang giữ chỗ chưa bị tiêu thụ nhưng đã chiếm 1 suất của voucher.
        int totalAvailable = totalGranted - totalUsed - totalReserved;

        return new VoucherStatsResponse
        {
            VoucherId = voucher.Id,
            Code = voucher.Code,
            Name = voucher.Name,
            TotalGranted = totalGranted,
            TotalUsed = totalUsed,
            TotalReserved = totalReserved,
            TotalAvailable = totalAvailable,
            UsageLimit = voucher.UsageLimit,
            UsedCount = voucher.UsedCount,
            TotalDiscountAmount = 0m // TODO: aggregate từ Order nếu cần
        };
    }

    // ============================================
    // APPLY (User) - Logic chính
    // ============================================

    public async Task<ApiResponse> ApplyVoucherAsync(string userId, ApplyVoucherRequest request)
    {
        // ===== 1. VALIDATE INPUT =====
        if (string.IsNullOrWhiteSpace(request.Code))
            return Fail("Mã voucher không được để trống.");

        if (request.OrderTotal <= 0)
            return Fail("Tổng đơn hàng phải lớn hơn 0.");

        string code = request.Code.Trim().ToUpperInvariant();

        // ===== 2. TÌM VOUCHER =====
        var voucher = await _voucherRepository.GetByCodeAsync(code);
        if (voucher == null)
            return Fail($"Mã voucher '{code}' không tồn tại.", ApiErrorCode.NotFound);

        // ===== 3. CHECK USER ĐÃ ĐƯỢC PHÁT VOUCHER CHƯA =====
        var userVoucher = await _userVoucherRepository.GetByUserAndVoucherAsync(userId, voucher.Id);
        if (userVoucher == null)
            return Fail("Bạn chưa được phát mã voucher này.", ApiErrorCode.Forbidden);

        // ===== 4. CHECK TRẠNG THÁI LƯỢT VOUCHER CỦA USER =====
        // Available = còn dùng được. Reserved = đang giữ chỗ cho đơn khác (chưa dùng thật).
        // Used = đã dùng thật và hết lượt cho user này.
        if (userVoucher.Status == UserVoucherStatus.Reserved)
            return Fail("Mã voucher này đang được giữ chỗ cho một đơn hàng khác.");

        if (userVoucher.Status == UserVoucherStatus.Used)
            return Fail("Bạn đã sử dụng mã voucher này rồi.");

        // ===== 4b. CHECK GIỚI HẠN SỬ DỤNG MỖI USER =====
        if (voucher.UsageLimitPerUser > 0 && userVoucher.UsageCount >= voucher.UsageLimitPerUser)
            return Fail(
                $"Bạn đã dùng mã voucher này {userVoucher.UsageCount}/{voucher.UsageLimitPerUser} lần, không thể dùng thêm.");

        // ===== 5. CHECK ACTIVE =====
        if (!voucher.IsActive)
            return Fail("Voucher đang tạm dừng, vui lòng thử lại sau.");

        // ===== 6. CHECK THỜI HẠN =====
        var now = DateTime.UtcNow;
        if (now < voucher.StartDate)
            return Fail($"Voucher chưa đến ngày áp dụng (bắt đầu từ {voucher.StartDate:dd/MM/yyyy}).");

        if (now > voucher.ExpiryDate)
            return Fail("Voucher đã hết hạn.");

        // ===== 7. CHECK TỔNG ĐƠN =====
        if (request.OrderTotal < voucher.MinOrderAmount)
            return Fail(
                $"Đơn hàng phải từ {voucher.MinOrderAmount:N0}đ trở lên để dùng voucher này.");

        // ===== 8. CHECK CÒN LƯỢT =====
        // Tổng lượt đã chiếm = đã dùng thật + đang giữ chỗ. Cả 2 đều chặn lượt mới.
        int claimedCount = voucher.UsedCount + voucher.ReservedCount;
        if (voucher.UsageLimit > 0 && claimedCount >= voucher.UsageLimit)
            return Fail("Voucher đã hết lượt sử dụng.");

        // ===== 9. TÍNH DISCOUNT =====
        decimal rawDiscount = CalculateRawDiscount(voucher, request.OrderTotal);
        decimal finalDiscount = ClampDiscount(rawDiscount, voucher.MinDiscount, voucher.MaxDiscount, request.OrderTotal);

        decimal finalTotal = Math.Max(0, request.OrderTotal - finalDiscount);

        return new ApiResponse
        {
            Success = true,
            Message = "Áp dụng voucher thành công.",
            Data = new ApplyVoucherResponse
            {
                VoucherId = voucher.Id,
                Code = voucher.Code,
                Name = voucher.Name,
                DiscountType = voucher.Type.ToString(),
                DiscountValue = voucher.Value,
                MinOrderAmount = voucher.MinOrderAmount,
                MinDiscount = voucher.MinDiscount,
                MaxDiscount = voucher.MaxDiscount,
                RawDiscount = rawDiscount,
                FinalDiscount = finalDiscount,
                OriginalTotal = request.OrderTotal,
                FinalTotal = finalTotal,
                Message = $"Bạn được giảm {finalDiscount:N0}đ"
            }
        };
    }

    /// <summary>
    /// GIỮ CHỖ voucher cho đơn hàng (Available → Reserved).
    /// </summary>
    /// <remarks>
    /// KHÔNG phải dùng thật: UsedCount của voucher không tăng ở bước này.
    /// Chỉ khi thanh toán thành công mới gọi <see cref="ConsumeForOrderAsync"/>.
    ///
    /// Thứ tự thao tác (cân bằng 2 collection, không dùng transaction):
    /// 1. Chiếm lượt voucher của user (atomic, filter Status = Available).
    /// 2. Tăng reservedCount của voucher (atomic, tôn trọng UsageLimit).
    /// 3. Nếu bước 2 thất bại (hết lượt) → hoàn lại lượt voucher đã chiếm ở bước 1.
    /// </remarks>
    public async Task<bool> ReserveForOrderAsync(string userId, string voucherId, string orderId)
    {
        // Bước 1: chiếm lượt voucher của user này (Available -> Reserved).
        // Atomic nên 2 đơn cùng lúc không thể cùng chiếm 1 voucher.
        var voucher = await _voucherRepository.GetByIdAsync(voucherId);
        int usageLimitPerUser = voucher?.UsageLimitPerUser ?? 0;

        if (!await _userVoucherRepository.TryReserveAsync(
                userId, voucherId, orderId, usageLimitPerUser))
        {
            // Voucher đã bị đơn khác giữ chỗ/dùng, user vượt giới hạn mỗi user,
            // hoặc user không có voucher này.
            return false;
        }

        // Bước 2: giữ chỗ 1 lượt trên toàn hệ thống (atomic, tôn trọng UsageLimit).
        // Nếu đã hết lượt thì trả lại bản ghi UserVoucher vừa chiếm.
        if (!await _voucherRepository.TryIncrementReservedCountAsync(voucherId))
        {
            await _userVoucherRepository.TryReleaseAsync(userId, voucherId, orderId);
            return false;
        }

        return true;
    }

    /// <summary>
    /// GIẢI PHÓNG voucher đang giữ chỗ (Reserved → Available).
    /// </summary>
    /// <remarks>
    /// Chỉ giải phóng được lượt voucher đang gắn với đúng orderId này.
    /// An toàn khi gọi lại nhiều lần (lần sau trả về false, không giải phóng nhầm).
    /// </remarks>
    public async Task<bool> ReleaseForOrderAsync(string userId, string voucherId, string orderId)
    {
        // Chỉ trả lại voucher mà đơn này thực sự đang giữ (atomic theo orderId).
        if (!await _userVoucherRepository.TryReleaseAsync(userId, voucherId, orderId))
            return false;

        // Trả lại 1 lượt giữ chỗ.
        await _voucherRepository.DecrementReservedCountAsync(voucherId);
        return true;
    }

    /// <summary>
    /// GHI NHẬN DÙNG THẬT voucher sau khi thanh toán thành công (Reserved → Used).
    /// </summary>
    /// <remarks>
    /// Idempotent: gọi lại nhiều lần (webhook lặp) chỉ ghi nhận 1 lần vì
    /// lần sau lượt voucher không còn ở trạng thái Reserved cho đơn này.
    ///
    /// Thứ tự thao tác (cân bằng 2 collection, không dùng transaction):
    /// 1. Chuyển lượt voucher của user Reserved → Used + tăng UsageCount của user.
    /// 2. Ghi nhận dùng thật trên voucher (usedCount +1, reservedCount -1).
    /// 3. Nếu bước 2 thất bại → hoàn lại lượt voucher (Reserved → Available)
    ///    và giảm UsageCount của user để không tính nhầm lượt đã dùng.
    /// </remarks>
    public async Task<bool> ConsumeForOrderAsync(string userId, string voucherId, string orderId)
    {
        var voucher = await _voucherRepository.GetByIdAsync(voucherId);
        int usageLimitPerUser = voucher?.UsageLimitPerUser ?? 0;

        // Bước 1: ghi nhận dùng thật ở phía user (atomic).
        if (!await _userVoucherRepository.TryConsumeAsync(
                userId, voucherId, orderId, usageLimitPerUser))
        {
            // Voucher không còn giữ chỗ cho đơn này (đã dùng, đã giải phóng,
            // hoặc thuộc đơn khác) -> không ghi nhận lần nữa.
            return false;
        }

        // Bước 2: ghi nhận dùng thật ở phía voucher (atomic, tôn trọng UsageLimit).
        if (!await _voucherRepository.TryIncrementUsedCountAsync(voucherId))
        {
            // Không ghi nhận được -> hoàn lại lượt voucher + trừ UsageCount của user
            // để trạng thái 2 collection không bị lệch.
            await _userVoucherRepository.TryReleaseAsync(userId, voucherId, orderId);
            await _userVoucherRepository.DecrementUsageCountAsync(userId, voucherId);
            return false;
        }

        return true;
    }

    // ============================================
    // BACKGROUND
    // ============================================

    public async Task<int> DisableExpiredVouchersAsync()
    {
        var expiredVouchers = await _voucherRepository.GetExpiredActiveVouchersAsync(100);

        int count = 0;
        foreach (var voucher in expiredVouchers)
        {
            voucher.IsActive = false;
            voucher.UpdatedAt = DateTime.UtcNow;
            await _voucherRepository.UpdateAsync(voucher);
            count++;
        }

        return count;
    }

    // ============================================
    // HELPER
    // ============================================

    /// <summary>
    /// Tính số tiền giảm thô (chưa áp Min/Max).
    /// </summary>
    private static decimal CalculateRawDiscount(Voucher voucher, decimal orderTotal)
    {
        return voucher.Type switch
        {
            VoucherType.Percentage => orderTotal * voucher.Value / 100m,
            VoucherType.FixedAmount => voucher.Value,
            _ => 0m
        };
    }

    /// <summary>
    /// Clamp discount theo Min/Max và theo tổng đơn:
    /// 1. Áp Max (nếu có) - giảm xuống nếu vượt trần.
    /// 2. Áp Min (nếu có) - nâng lên nếu dưới sàn.
    /// 3. Không được vượt quá tổng đơn (nếu không TotalAmount sẽ âm).
    /// </summary>
    private static decimal ClampDiscount(
        decimal rawDiscount, decimal minDiscount, decimal maxDiscount, decimal orderTotal)
    {
        decimal final = rawDiscount;

        if (maxDiscount > 0 && final > maxDiscount)
            final = maxDiscount;

        if (minDiscount > 0 && final < minDiscount)
            final = minDiscount;

        // Discount không được lớn hơn tổng đơn, nếu không đơn sẽ có tổng tiền âm
        // và số tiền gửi PayOS cũng âm.
        if (final > orderTotal)
            final = orderTotal;

        return Math.Round(final, 0); // Làm tròn về VND
    }

    /// <summary>
    /// Map entity Voucher sang DTO response.
    /// Tính sẵn các field phái sinh để FE không phải tự tính.
    /// </summary>
    private static VoucherResponse MapToResponse(Voucher voucher)
    {
        var now = DateTime.UtcNow;
        bool isWithinDate = now >= voucher.StartDate && now <= voucher.ExpiryDate;
        // Lượt đã chiếm = đã dùng thật + đang giữ chỗ. Cả 2 đều chặn lượt mới.
        int claimedCount = voucher.UsedCount + voucher.ReservedCount;
        bool hasRemaining = voucher.UsageLimit <= 0 || claimedCount < voucher.UsageLimit;
        bool isUsable = voucher.IsActive && isWithinDate && hasRemaining;

        string status;
        if (!voucher.IsActive)
            status = "inactive";
        else if (now > voucher.ExpiryDate)
            status = "expired";
        else if (now < voucher.StartDate)
            status = "upcoming";
        else if (!hasRemaining)
            status = "exhausted";
        else
            status = "active";

        int remaining = voucher.UsageLimit <= 0
            ? -1
            : Math.Max(0, voucher.UsageLimit - claimedCount);

        return new VoucherResponse
        {
            Id = voucher.Id,
            Code = voucher.Code,
            Name = voucher.Name,
            Description = voucher.Description,
            Type = voucher.Type.ToString(),
            Value = voucher.Value,
            MinOrderAmount = voucher.MinOrderAmount,
            MinDiscount = voucher.MinDiscount,
            MaxDiscount = voucher.MaxDiscount,
            StartDate = voucher.StartDate,
            ExpiryDate = voucher.ExpiryDate,
            UsageLimit = voucher.UsageLimit,
            UsedCount = voucher.UsedCount,
            ReservedCount = voucher.ReservedCount,
            UsageLimitPerUser = voucher.UsageLimitPerUser,
            IsActive = voucher.IsActive,
            CreatedBy = voucher.CreatedBy,
            CreatedAt = voucher.CreatedAt,
            UpdatedAt = voucher.UpdatedAt,
            IsWithinDateRange = isWithinDate,
            HasRemainingUsage = hasRemaining,
            Status = status,
            RemainingUsage = remaining
        };
    }

    private static ApiResponse Fail(string message, string errorCode = ApiErrorCode.Validation)
        => new ApiResponse { Success = false, Message = message, ErrorCode = errorCode };
}
