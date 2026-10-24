using MongoDB.Bson;
using REWEAR.Application.DTOs;
using REWEAR.Application.Interfaces;
using REWEAR.Domain.Entities;
using REWEAR.Domain.Enums;

namespace REWEAR.Application.Services;

/// <summary>
/// Service quản lý "Kho voucher của tôi" (Task 10).
/// </summary>
public class UserVoucherService : IUserVoucherService
{
    private readonly IUserVoucherRepository _userVoucherRepository;
    private readonly IVoucherRepository _voucherRepository;

    public UserVoucherService(
        IUserVoucherRepository userVoucherRepository,
        IVoucherRepository voucherRepository)
    {
        _userVoucherRepository = userVoucherRepository;
        _voucherRepository = voucherRepository;
    }

    public async Task<List<UserVoucherResponse>> GetMyCollectionAsync(string userId, string filter = "all")
    {
        var userVouchers = await _userVoucherRepository.GetByUserIdAsync(userId);

        if (userVouchers.Count == 0)
            return new List<UserVoucherResponse>();

        // Lấy thông tin voucher tương ứng
        var voucherIds = userVouchers.Select(uv => uv.VoucherId).Distinct().ToList();
        var vouchers = new Dictionary<string, Voucher>();

        foreach (var vid in voucherIds)
        {
            var v = await _voucherRepository.GetByIdAsync(vid);
            if (v != null) vouchers[vid] = v;
        }

        var now = DateTime.UtcNow;
        var result = new List<UserVoucherResponse>();

        foreach (var uv in userVouchers)
        {
            if (!vouchers.TryGetValue(uv.VoucherId, out var voucher))
                continue; // Voucher đã bị xóa cứng → bỏ qua (không hiển thị)

            var status = ComputeStatus(uv, voucher, now);
            bool isUsable = status == "available";

            // Lọc theo filter
            if (!ShouldIncludeByFilter(status, filter))
                continue;

            result.Add(new UserVoucherResponse
            {
                Id = uv.Id,
                VoucherId = voucher.Id,
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
                IsUsed = uv.IsUsed,
                UsedAt = uv.UsedAt,
                ReservedAt = uv.ReservedAt,
                UsageCount = uv.UsageCount,
                UsageLimitPerUser = voucher.UsageLimitPerUser,
                OrderId = uv.OrderId,
                GrantedAt = uv.GrantedAt,
                Status = status,
                IsUsable = isUsable
            });
        }

        // Sắp xếp: usable trước, sau đó theo ngày cấp mới nhất
        return result
            .OrderByDescending(r => r.IsUsable)
            .ThenByDescending(r => r.GrantedAt)
            .ToList();
    }

    public async Task<UserVoucherResponse?> GetByIdAsync(string id, string userId)
    {
        if (!ObjectId.TryParse(id, out _))
            return null;

        var userVoucher = await _userVoucherRepository.GetByIdAsync(id);
        if (userVoucher == null || userVoucher.UserId != userId)
            return null; // Bảo mật: không cho xem của user khác

        var voucher = await _voucherRepository.GetByIdAsync(userVoucher.VoucherId);
        if (voucher == null)
            return null; // Voucher đã bị xóa cứng

        var now = DateTime.UtcNow;
        var status = ComputeStatus(userVoucher, voucher, now);

        return new UserVoucherResponse
        {
            Id = userVoucher.Id,
            VoucherId = voucher.Id,
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
            IsUsed = userVoucher.IsUsed,
            UsedAt = userVoucher.UsedAt,
            ReservedAt = userVoucher.ReservedAt,
            UsageCount = userVoucher.UsageCount,
            UsageLimitPerUser = voucher.UsageLimitPerUser,
            OrderId = userVoucher.OrderId,
            GrantedAt = userVoucher.GrantedAt,
            Status = status,
            IsUsable = status == "available"
        };
    }

    /// <summary>
    /// Tính trạng thái hiển thị của 1 UserVoucher.
    /// </summary>
    /// <remarks>
    /// Phân biệt rõ 3 trạng thái vòng đời:
    /// - "reserved": đang giữ chỗ cho 1 đơn chờ thanh toán — CHƯA dùng thật.
    /// - "used": đã dùng thật (đơn đã thanh toán thành công) và hết lượt cho user.
    /// - "available": còn dùng được (còn hạn + chưa giữ chỗ + chưa hết lượt).
    /// Ngoài ra có "expired" / "upcoming" / "exhausted" theo điều kiện voucher.
    /// </remarks>
    private static string ComputeStatus(UserVoucher uv, Voucher voucher, DateTime now)
    {
        // Đang giữ chỗ cho 1 đơn chờ thanh toán: chưa tiêu thụ thật.
        if (uv.Status == UserVoucherStatus.Reserved)
            return "reserved";

        // Đã dùng thật và hết lượt cho user này.
        if (uv.Status == UserVoucherStatus.Used)
            return "used";

        // Còn lượt nhưng đã dùng đủ UsageLimitPerUser lần -> không dùng thêm được.
        if (voucher.UsageLimitPerUser > 0 && uv.UsageCount >= voucher.UsageLimitPerUser)
            return "exhausted";

        if (!voucher.IsActive)
            return "expired";

        if (now > voucher.ExpiryDate)
            return "expired";

        if (now < voucher.StartDate)
            return "upcoming";

        // Lượt đã chiếm trên toàn hệ thống = đã dùng thật + đang giữ chỗ.
        int claimedCount = voucher.UsedCount + voucher.ReservedCount;
        if (voucher.UsageLimit > 0 && claimedCount >= voucher.UsageLimit)
            return "exhausted";

        return "available";
    }

    private static bool ShouldIncludeByFilter(string status, string filter)
    {
        return filter?.ToLowerInvariant() switch
        {
            "all" => true,
            "available" => status == "available",
            "used" => status == "used",
            "reserved" => status == "reserved",
            "expired" => status == "expired" || status == "exhausted" || status == "upcoming",
            _ => true
        };
    }
}
